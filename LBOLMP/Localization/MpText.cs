namespace LBOLMP
{
    /// <summary>
    /// Every piece of interface text the mod can show.
    ///
    /// These are keys, not words. The text itself lives in Resources/Ui*.yaml, one file per
    /// language, so translators never have to open a .cs file. Adding a key here means adding a
    /// matching line to at least UiEn.yaml.
    /// </summary>
    public enum MpText
    {
        // ------------------------------------------------------------------ lobby window
        LobbyWindowTitle,
        LobbyNotConnected,
        LobbyYourName,
        LobbyDirectConnection,
        LobbyPort,
        LobbyHostSession,
        LobbyHostAddress,
        LobbyJoinSession,
        LobbyPortNotANumber,
        LobbyOfflineHelp,
        LobbySteam,
        LobbySteamUnavailable,
        LobbyHostOverSteam,
        LobbySteamHelp,
        LobbyHosting,
        LobbyConnected,
        LobbyHostingDirectIp,
        LobbyHostingSteam,
        LobbyConnectedDirectIp,
        LobbyConnectedSteam,
        LobbyInviteFriends,
        LobbyTagHost,
        LobbyTagYou,
        LobbyChoosing,
        LobbyPlayerRow,
        LobbyLockedIn,
        LobbySeed,
        LobbyMapVoteHint,
        LobbyLeaveSession,
        LobbyBalanceSettings,

        // ------------------------------------------------------------------ mismatched sideloaded content
        ModMismatchTitle,
        ModMismatchHelp,
        ModMismatchRow,
        ModMismatchNotice,
        ModMissingFor,
        ModDiffersFor,
        ModYou,
        ModKindCharacters,
        ModKindCards,
        ModKindExhibits,
        ModKindEnemies,
        ModKindAdventures,

        // ------------------------------------------------------------------ balance settings window
        SettingsWindowTitle,
        SettingsIntro,
        SettingsHostNote,
        SettingsNextRunNote,
        SettingsLockedForThisRun,
        SettingsDefault,
        SettingsResetAll,
        SettingsClose,
        SettingsNothingToShow,

        SettingEnemyHpScaleName,
        SettingEnemyHpScaleHelp,
        SettingEscalationName,
        SettingEscalationHelp,
        SettingReviveHpName,
        SettingReviveHpHelp,
        SettingResilienceName,
        SettingResilienceHelp,
        SettingMultiplayerCardsName,

        // ------------------------------------------------------------------ corner HUD
        HudParty,
        HudPlayerRow,
        HudVoteNode,
        HudVoteStillChoosing,

        // ------------------------------------------------------------------ player state
        StateInLobby,
        StateReady,
        StateResuming,
        StateHp,
        StateDisconnected,

        // ------------------------------------------------------------------ battle board
        BoardDefeated,
        BoardSittingOut,
        BoardWaitingForOne,
        BoardWaitingForOneToFinish,
        BoardWaitingForMany,
        BoardLostContact,
        BoardBlock,
        BoardShield,
        ActivitySpectating,
        ActivityDownSpectating,
        ActivityDone,
        ActivityDown,
        ActivityTurnOver,
        ActivityHand,

        // ------------------------------------------------------------------ emotes
        EmoteNice,
        EmoteNegative,
        EmoteHurryUp,

        // ------------------------------------------------------------------ map voting
        MapWaitingForOne,
        MapWaitingForMany,
        MapMovingToNode,
        MapPartySplit,
        MapPick,
        MapHeadingTo,
        RestartPartyMoving,

        // ------------------------------------------------------------------ inspecting a hand
        InspectBanner,
        InspectZoneTitle,

        // ------------------------------------------------------------------ session status
        StatusHostFailed,
        StatusHostingOnPort,
        StatusHostSteamFailed,
        StatusOpeningSteamLobby,
        StatusHostingOverSteam,
        StatusConnecting,
        StatusConnectFailed,
        StatusAlreadyInSession,
        StatusConnectingSteam,
        StatusConnectSteamFailed,
        StatusConnectedAsPlayer,
        StatusRejected,
        StatusWaitingForPlayers,
        StatusWaitingForNames,
        StatusWaitingToResume,
        StatusRunStarted,
        StatusRunResumed,
        StatusBackInLobby,
        NoticeResumeStaggered,
        StatusPlayerLeft,
        StatusDisconnected,
        StatusSteamLobbyFailed,
        StatusSteamJoinFailed,

        // ------------------------------------------------------------------ error/networking messages that are human-readable
        ReasonProtocolMismatch,
        ReasonRunInProgress,
        ReasonStartSplit,
        ReasonResumeDifferentRuns,
        ReasonSessionFull,
        ReasonYouLeft,
        ReasonRemoteClosed,
        ReasonConnectionFailed,
        ReasonSteamClosed,
        ReasonReadFailed,
        ReasonWriteFailed,
        ReasonSendFailed,
        ReasonTimedOut,

        // ------------------------------------------------------------------ transports
        ErrorSteamUnavailable,
        ErrorSteamListenFailed,
        ErrorSteamConnectFailed
    }
}
