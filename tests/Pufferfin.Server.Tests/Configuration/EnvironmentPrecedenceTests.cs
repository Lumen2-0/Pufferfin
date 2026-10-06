using System.Collections.Generic;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Tests.TestHelpers;
using Xunit;

namespace Pufferfin.Server.Tests.Configuration;

/// <summary>
/// The central trap: a redeployment must never silently overwrite a setting changed in the admin interface.
/// Each "restart" builds a new store on the same configuration directory with the environment of that start.
/// </summary>
public sealed class EnvironmentPrecedenceTests
{
    private const string OldUrl = "http://radarr-old:7878";
    private const string NewUrl = "http://radarr-new:7878";

    private static readonly Dictionary<string, string> _oldEnvironment = new() { [SettingKeys.RadarrUrl] = OldUrl };

    [Fact]
    public void FirstStart_EnvironmentSeedsTheStore_AsReadOnly()
    {
        using var context = new StoreTestContext();

        var store = context.Start(_oldEnvironment);

        var state = store.Current.StatesByKey[SettingKeys.RadarrUrl];
        Assert.Equal(OldUrl, state.Value);
        Assert.Equal(SettingSource.Environment, state.Source);
        Assert.True(state.IsReadOnly);

        // Seeded values are persisted: the store, not the environment, holds them from now on.
        var restarted = context.Start();
        Assert.Equal(OldUrl, restarted.Current.StatesByKey[SettingKeys.RadarrUrl].Value);
    }

    [Fact]
    public void ValueChangedInUi_IsNotOverwritten_ByRestartWithOldEnvironmentVariableStillPresent()
    {
        using var context = new StoreTestContext();

        // Deployed with the variable, then the variable is dropped and the admin edits the value in the UI.
        context.Start(_oldEnvironment);
        var released = context.Start();
        Assert.False(released.Current.StatesByKey[SettingKeys.RadarrUrl].IsReadOnly);
        released.Update(StoreTestContext.Changes((SettingKeys.RadarrUrl, NewUrl)));

        // Redeployed with the old .env still holding the old value.
        var restarted = context.Start(_oldEnvironment);

        var state = restarted.Current.StatesByKey[SettingKeys.RadarrUrl];
        Assert.Equal(NewUrl, state.Value);
        Assert.Equal(SettingSource.Store, state.Source);
        Assert.True(state.EnvironmentIgnored);
        Assert.False(state.IsReadOnly);

        // And again: the old variable never wins, however many restarts.
        var restartedAgain = context.Start(_oldEnvironment);
        Assert.Equal(NewUrl, restartedAgain.Current.StatesByKey[SettingKeys.RadarrUrl].Value);
    }

    [Fact]
    public void ValueSetInUi_IsNotOverwritten_WhenTheVariableAppearsLater()
    {
        using var context = new StoreTestContext();

        context.Start().Update(StoreTestContext.Changes((SettingKeys.RadarrUrl, NewUrl)));
        var restarted = context.Start(_oldEnvironment);

        Assert.Equal(NewUrl, restarted.Current.StatesByKey[SettingKeys.RadarrUrl].Value);
        Assert.True(restarted.Current.StatesByKey[SettingKeys.RadarrUrl].EnvironmentIgnored);
    }

    [Fact]
    public void ValueClearedInUi_StaysCleared_WhenTheVariableIsStillPresent()
    {
        using var context = new StoreTestContext();

        context.Start(_oldEnvironment);
        context.Start().Update(StoreTestContext.Changes((SettingKeys.RadarrUrl, null)));
        var restarted = context.Start(_oldEnvironment);

        var state = restarted.Current.StatesByKey[SettingKeys.RadarrUrl];
        Assert.Null(state.Value);
        Assert.Equal(SettingSource.Store, state.Source);
        Assert.False(restarted.Current.Settings.IsEnabled(PufferfinService.Radarr));
    }

    [Fact]
    public void EnvironmentValueNeverEdited_FollowsTheVariable_OnRedeploy()
    {
        using var context = new StoreTestContext();

        context.Start(_oldEnvironment);
        var restarted = context.Start(new Dictionary<string, string> { [SettingKeys.RadarrUrl] = NewUrl });

        var state = restarted.Current.StatesByKey[SettingKeys.RadarrUrl];
        Assert.Equal(NewUrl, state.Value);
        Assert.Equal(SettingSource.Environment, state.Source);
    }

    [Fact]
    public void EnvironmentVariableRemoved_KeepsTheValue_AndMakesItEditable()
    {
        using var context = new StoreTestContext();

        context.Start(_oldEnvironment);
        var restarted = context.Start();

        var state = restarted.Current.StatesByKey[SettingKeys.RadarrUrl];
        Assert.Equal(OldUrl, state.Value);
        Assert.Equal(SettingSource.Store, state.Source);
        Assert.False(state.EnvironmentIgnored);
    }

    [Fact]
    public void ReadOnlySetting_RejectsAnUpdate_AndNothingIsSaved()
    {
        using var context = new StoreTestContext();
        var store = context.Start(_oldEnvironment);

        var exception = Assert.Throws<SettingsValidationException>(
            () => store.Update(StoreTestContext.Changes((SettingKeys.RadarrUrl, NewUrl), (SettingKeys.SonarrUrl, NewUrl))));

        Assert.Contains(SettingKeys.RadarrUrl, exception.Errors.Keys);
        Assert.Equal(OldUrl, store.Current.StatesByKey[SettingKeys.RadarrUrl].Value);

        // All or nothing: the valid change in the same request was not applied either.
        Assert.Null(context.Start(_oldEnvironment).Current.StatesByKey[SettingKeys.SonarrUrl].Value);
    }

    [Fact]
    public void InvalidEnvironmentValue_IsNotSeeded_AndIsLoggedWithoutItsValue()
    {
        using var context = new StoreTestContext();

        var store = context.Start(new Dictionary<string, string> { [SettingKeys.SeedMinDays] = "three-days" });

        Assert.Equal(SettingSource.Unset, store.Current.StatesByKey[SettingKeys.SeedMinDays].Source);
        Assert.DoesNotContain(context.Logs.Entries, e => e.Contains("three-days", System.StringComparison.Ordinal));
    }
}
