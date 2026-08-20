using System.Collections.Generic;

namespace LBOLMP
{
    /// <summary>
    /// Translation for the mod.
    /// English, Simplified Chinese, Traditional Chinese, Japanese, always in that order.
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

    /// <summary>
    /// Contains text for every language supported by the mod.
    /// </summary>
    internal readonly struct MpPhrase
    {
        internal readonly string En;
        internal readonly string ZhHans;
        internal readonly string ZhHant;
        internal readonly string Ja;

        internal MpPhrase(string en, string zhHans, string zhHant, string ja)
        {
            En = en;
            ZhHans = zhHans;
            ZhHant = zhHant;
            Ja = ja;
        }
    }

    internal static class MpStrings
    {
        /// <summary>
        /// English, Simplified Chinese, Traditional Chinese, Japanese — in that order, always.
        ///
        /// The fourth argument is deliberately not optional. A translator working through this file
        /// should see an empty row waiting for them at every phrase, not a phrase that quietly has
        /// no Japanese at all.
        /// </summary>
        private static MpPhrase P(string en, string zhHans, string zhHant, string ja) =>
            new MpPhrase(en, zhHans, zhHant, ja);

        internal static readonly Dictionary<MpText, MpPhrase> Table = new Dictionary<MpText, MpPhrase>
        {
            // ------------------------------------------------------------------ lobby window
            [MpText.LobbyWindowTitle] = P(
                "LBOL MP  v{0}",
                "LBOL MP  v{0}",
                "LBOL MP  v{0}",
                "LBOL MP  v{0}"),
            [MpText.LobbyNotConnected] = P(
                "Not connected",
                "未连接",
                "未連接",
                "未接続"),
            [MpText.LobbyYourName] = P(
                "Your name",
                "你的名字",
                "你的名字",
                "プレイヤー名"),
            [MpText.LobbyDirectConnection] = P(
                "Direct connection",
                "直接连接",
                "直接連接",
                "直接接続"),
            [MpText.LobbyPort] = P(
                "Port",
                "端口",
                "端口",
                "ポート"),
            [MpText.LobbyHostSession] = P(
                "Host session",
                "创建房间",
                "建立房間",
                "セッションをホスト"),
            [MpText.LobbyHostAddress] = P(
                "Host address",
                "房主 IP 地址",
                "房主 IP 位址",
                "ホストアドレス"),
            [MpText.LobbyJoinSession] = P(
                "Join session",
                "加入房间",
                "加入房間",
                "セッションに参加"),
            [MpText.LobbyPortNotANumber] = P(
                "Port is not a number",
                "端口必须是数字",
                "端口必須是數字",
                "ポート番号は数字でなければなりません"),
            [MpText.LobbyOfflineHelp] = P(
                "Host should host a session, have everyone else join, then all of you pick a character on the Start Game screen. The run begins once everybody has confirmed.",
                "由房主建立房间，其他人加入，然后所有人在正常的开始游戏画面选择角色。所有人确认后开始游戏。",
                "由房主建立房間，其他人加入，然後所有人在正常的開始遊戲畫面選擇角色。所有人確認後開始遊戲。",
                "ホストがセッションを作成し、全員が参加してください。キャラクターを選択して全員が準備完了すると、ゲームが開始されます。"),
            [MpText.LobbySteam] = P(
                "Steam",
                "Steam",
                "Steam",
                "Steam"),
            [MpText.LobbySteamUnavailable] = P(
                "Steam is currently not available, only direct IP connections can be used.",
                "当前无法使用 Steam，只能使用直接 IP 连接。",
                "目前無法使用 Steam，只能使用直接 IP 連接。",
                "現在Steamを利用できないため、直接IP接続のみ使用できます。"),
            [MpText.LobbyHostOverSteam] = P(
                "Host over Steam",
                "通过 Steam 创建房间",
                "通過 Steam 建立房間",
                "Steamでホスト"),
            [MpText.LobbySteamHelp] = P(
                "Once the session is hosted, invite friends from the Steam overlay. To join someone else, accept their invite or join via Steam.",
                "创建房间后，可从 Steam 介面邀请好友。请接受对方的邀请以加入房间，或通过 Steam 加入房间。",
                "建立房間後，可從 Steam 介面邀請好友。請接受對方的邀請以加入房間，或透過 Steam 加入房間。",
                "セッションをホストしたら、Steamオーバーレイからフレンドを招待してください。参加する場合は招待を受けるかSteamから参加してください。"),
            [MpText.LobbyHosting] = P(
                "Hosting",
                "主持中",
                "主持中",
                "ホスト中"),
            [MpText.LobbyConnected] = P(
                "Connected",
                "已连接",
                "已連接",
                "接続済み"),
            // port.
            [MpText.LobbyHostingDirectIp] = P(
                "Hosting over direct IP, port {0}",
                "正在通过直接 IP 主持房间，端口 {0}",
                "正在透過直接 IP 主持房間，端口 {0}",
                "直接IPでホスト中（ポート {0}"),
            [MpText.LobbyHostingSteam] = P(
                "Hosting over Steam",
                "正在通过 Steam 主持房间",
                "正在透過 Steam 主持房間",
                "Steamでホスト中"),
            // address, port.
            [MpText.LobbyConnectedDirectIp] = P(
                "Connected over direct IP to {0}:{1}",
                "已通过直接 IP 连接到 {0}：{1}",
                "已透過直接 IP 連接到 {0}：{1}",
                "{0}:{1}に直接IPで接続済み"),
            // host name.
            [MpText.LobbyConnectedSteam] = P(
                "Connected over Steam to {0}",
                "已通过 Steam 连接到房主 {0}",
                "已透過 Steam 連接到房主 {0}",
                "Steam経由で{0}に接続"),
            [MpText.LobbyInviteFriends] = P(
                "Invite friends...",
                "邀请好友⋯⋯",
                "邀請好友⋯⋯",
                "フレンドを招待・・・"),
            [MpText.LobbyTagHost] = P(
                " [host]",
                " [房主]",
                " [房主]",
                " [ホスト]"),
            [MpText.LobbyTagYou] = P(
                " [you]",
                " [你]",
                " [你]",
                " [自分]"),
            [MpText.LobbyChoosing] = P(
                "choosing...",
                "选择中⋯⋯",
                "選擇中⋯⋯",
                "選択中・・・"),
            // #id, name, tags, character, state.
            [MpText.LobbyPlayerRow] = P(
                "#{0}  {1}{2}   {3}   {4}",
                "#{0}  {1}{2}   {3}   {4}",
                "#{0}  {1}{2}   {3}   {4}",
                "#{0}  {1}{2}   {3}   {4}"),
            [MpText.LobbyLockedIn] = P(
                "Locked in. Waiting for everyone else to confirm their character.",
                "已锁定。等待其他人确认角色。",
                "已鎖定。等待其他人確認角色。",
                "確定しました。他のプレイヤーがキャラクターを確定するのを待っています。"),
            [MpText.LobbySeed] = P(
                "Seed {0}",
                "种子 {0}",
                "種子 {0}",
                "SEED {0}"),
            [MpText.LobbyMapVoteHint] = P(
                "Click a map node to vote for it. The party moves once everyone has voted for the same node.",
                "点击地图节点以投票。所有人投给同一个节点后，队伍才会前进。",
                "點擊地圖節點以投票。所有人投給同一個節點後，隊伍才會前進。",
                "マップのノードをクリックして投票してください。全員が同じノードに投票すると、パーティーが移動します。"),
            [MpText.LobbyLeaveSession] = P(
                "Leave session",
                "离开房间",
                "離開房間",
                "セッションから退出"),
            [MpText.LobbyBalanceSettings] = P(
                "Balance settings...",
                "平衡性设定⋯⋯",
                "平衡性設定⋯⋯",
                "バランス設定・・・"),

            // ------------------------------------------------------------------ balance settings window
            [MpText.SettingsWindowTitle] = P(
                "Balance settings",
                "平衡性设定",
                "平衡性設定",
                "バランス設定"),
            [MpText.SettingsIntro] = P(
                "How much harder the game gets with more players.",
                "人数越多，游戏难度提升多少。",
                "人數越多，遊戲難度提升多少。",
                "プレイヤーが増えることで、ゲームの難易度がどの程度上昇するかを設定します。"),
            [MpText.SettingsHostNote] = P(
                "All of these are decided by the host. If you join someone else's session, theirs are used and yours are ignored.",
                "以下所有设定均由房主决定。加入别人的房间时仅使用房主的设定。",
                "以下所有設定均由房主決定。加入別人的房間時僅使用房主的設定。",
                "これらの設定はすべてホストが決定します。他のプレイヤーのセッションに参加した場合は、ホストの設定が使用され、自分の設定は無視されます。"),
            [MpText.SettingsNextRunNote] = P(
                "Changes are saved straight away and apply from the next run onwards.",
                "修改会立即保存，并从下一局开始生效。",
                "修改會立即儲存，並從下一局開始生效。",
                "変更はすぐに保存され、次のランから適用されます。"),
            [MpText.SettingsLockedForThisRun] = P(
                "A run is in progress, changes won't apply until the next run.",
                "当前有正在进行的游戏。本局沿用开始时的数值，此处的修改将在下一局生效。",
                "目前有正在進行的遊戲。本局沿用開始時的數值，此處的修改將在下一局生效。",
                "現在ゲームが進行中です。このランでは開始時の設定が使用され、変更は次のランから適用されます。"),
            [MpText.SettingsDefault] = P(
                "Default",
                "预设",
                "預設",
                "デフォルト"),
            [MpText.SettingsResetAll] = P(
                "Reset all to defaults",
                "全部恢复预设",
                "全部恢復預設",
                "すべてデフォルトに戻す"),
            [MpText.SettingsClose] = P(
                "Close",
                "关闭",
                "關閉",
                "閉じる"),
            [MpText.SettingsNothingToShow] = P(
                "No balance settings were found.",
                "未找到平衡性设定。",
                "未找到平衡性設定。",
                "バランス設定が見つかりません。"),

            [MpText.SettingEnemyHpScaleName] = P(
                "Enemy health per extra player",
                "每位额外玩家的敌人生命值",
                "每位額外玩家的敵人生命值",
                "追加プレイヤー1人あたりの敵のHP"),
            [MpText.SettingEnemyHpScaleHelp] = P(
                "Extra max HP every enemy gets for each player beyond the first.",
                "除第一位玩家外，每多一位玩家，敌人最大生命值增加的比例。",
                "除第一位玩家外，每多一位玩家，敵人最大生命值增加的比例。",
                "1人目を超えるプレイヤー1人につき、すべての敵に追加される最大HPの割合です。"),
            // Act number.
            [MpText.SettingEscalationName] = P(
                "Act {0} escalation",
                "第 {0} 章额外加成",
                "第 {0} 章額外加成",
                "第 {0} 章の追加補正"),
            [MpText.SettingEscalationHelp] = P(
                "Extra enemy max HP in this act, on top of the setting above. This stacks an additional time for each extra player, adding progressively more HP with more players. 0 turns it off for this act.",
                "本章中在上一项设定之外额外增加的敌人最大生命值，并且会叠加：每多一位玩家，加成都比前一位多一份。设为 0.1 时，第 2 位玩家增加 +10%，第 3 位再增加 +20%，第 4 位再增加 +30%。与上一项相加，而非相乘。设为 0 则本章不启用。",
                "本章中在上一項設定之外額外增加的敵人最大生命值，並且會疊加：每多一位玩家，加成都比前一位多一份。設為 0.1 時，第 2 位玩家增加 +10%，第 3 位再增加 +20%，第 4 位再增加 +30%。與上一項相加，而非相乘。設為 0 則本章不啟用。",
                "この章では、上記の設定に加えて敵の最大HPがさらに増加します。追加プレイヤーが1人増えるごとにこの補正が追加で適用され、プレイヤーが多いほど増加量も大きくなります。0にすると、この章では無効になります。"),
            [MpText.SettingReviveHpName] = P(
                "Revive health",
                "复活时的生命值",
                "復活時的生命值",
                "復活時のHP"),
            [MpText.SettingReviveHpHelp] = P(
                "How much of their max health a defeated player comes back with when the party wins the fight. Between 0 and 1 (0%-100%). Players always revive with at least 1 HP.",
                "队伍获胜时，被击倒的玩家按最大生命值的多少比例复活。取值 0 到 1，且至少为 1 点生命。",
                "隊伍獲勝時，被擊倒的玩家按最大生命值的多少比例復活。取值 0 到 1，且至少為 1 點生命。",
                "パーティーが戦闘に勝利した際、倒されたプレイヤーが最大HPの何％で復活するかを設定します。"),
            [MpText.SettingResilienceName] = P(
                "Enemies are Resilient",
                "敌人拥有「坚韧」",
                "敵人擁有「堅韌」",
                "敵が「耐性」を持つ"),
            [MpText.SettingResilienceHelp] = P(
                "Give every enemy the Resilient status effect. For each extra player, enemies lose debuffs faster. Disable this to make debuffs decay as fast as in single player.",
                "让每个敌人获得「坚韧」状态。除第一位玩家外，每多一位玩家，敌人在回合结束时额外失去 1 层虚弱、易伤和锁定，并且获得的失去火力减少 1 点，但不低于 1 点。关闭后，减益效果与单人游戏时完全相同。",
                "讓每個敵人獲得「堅韌」狀態。除第一位玩家外，每多一位玩家，敵人在回合結束時額外失去 1 層虛弱、易傷和鎖定，並且獲得的失去火力減少 1 點，但不低於 1 點。關閉後，減益效果與單人遊戲時完全相同。",
                "すべての敵に「耐性」状態を付与します。プレイヤーが増えるほど、敵のデバフが早く解除されます。無効にすると、デバフの減少速度はシングルプレイ時と同じになります。"),

            // ------------------------------------------------------------------ corner HUD
            [MpText.HudParty] = P(
                "Party ({0})",
                "队伍 ({0})",
                "隊伍 ({0})",
                "パーティー（{0}）"),
            // name, hp, maxHp, vote.
            [MpText.HudPlayerRow] = P(
                "{0}  {1}/{2}{3}",
                "{0}  {1}/{2}{3}",
                "{0}  {1}/{2}{3}",
                "{0}  {1}/{2}{3}"),
            [MpText.HudVoteNode] = P(
                "  -> ({0},{1})",
                "  -> ({0},{1})",
                "  -> ({0},{1})",
                "  -> ({0},{1})"),
            [MpText.HudVoteStillChoosing] = P(
                "  -> still choosing",
                "  -> 仍在选择",
                "  -> 仍在選擇",
                "  -> 選択中"),

            // ------------------------------------------------------------------ player state
            [MpText.StateInLobby] = P(
                "in lobby",
                "在房间中",
                "在房間中",
                "ロビーにいます"),
            [MpText.StateReady] = P(
                "ready",
                "已准备",
                "已準備",
                "準備完了"),
            [MpText.StateResuming] = P(
                "ready to continue",
                "已准备继续",
                "已準備繼續",
                ""),
            [MpText.StateHp] = P(
                "{0}/{1} HP",
                "{0}/{1} 生命",
                "{0}/{1} 生命",
                "{0}/{1} HP"),
            [MpText.StateDisconnected] = P(
                "disconnected",
                "已失去连接",
                "已失去連接",
                "切断されました"),

            // ------------------------------------------------------------------ battle board
            [MpText.BoardDefeated] = P(
                "You've been defeated. Wait for your partners to finish the combat.",
                "你已被击败。等待队友结束这场战斗。",
                "你已被擊敗。等待隊友結束這場戰鬥。",
                "倒されました。仲間が戦闘を終えるまで待ってください。"),
            [MpText.BoardSittingOut] = P(
                "You're spectating this fight.",
                "你正在观战这场战斗。",
                "你正在觀戰這場戰鬥。",
                "この戦闘を観戦しています。"),
            [MpText.BoardWaitingForOne] = P(
                "Waiting for {0} to finish their turn",
                "等待 {0} 结束回合",
                "等待 {0} 結束回合",
                "{0}のターン終了を待っています"),
            [MpText.BoardWaitingForOneToFinish] = P(
                "Waiting for {0} to finish the fight",
                "等待 {0} 结束战斗",
                "等待 {0} 結束戰鬥",
                "{0}が戦闘を終えるのを待っています"),
            [MpText.BoardWaitingForMany] = P(
                "Waiting for {0}",
                "等待 {0}",
                "等待 {0}",
                "{0}を待っています"),
            [MpText.BoardLostContact] = P(
                "Connection with {0} lost. Continuing without them for now.",
                "连接已断开：{0}。 暂时先不等他们了。",
                "連接已中斷：{0}。 暫時先不等他們了。",
                "{0}との接続が切れました。ひとまず、そのプレイヤーなしで続行します。"),
            [MpText.BoardBlock] = P(
                "BLK {0}",
                "格挡 {0}",
                "格擋 {0}",
                "ブロック {0}"),
            [MpText.BoardShield] = P(
                "SHD {0}",
                "护盾 {0}",
                "護盾 {0}",
                "シールド {0}"),
            [MpText.ActivitySpectating] = P(
                "spectating",
                "观战中",
                "觀戰中",
                "観戦中"),
            [MpText.ActivityDownSpectating] = P(
                "down, spectating",
                "已倒下，观战中",
                "已倒下，觀戰中",
                "戦闘不能・観戦中"),
            [MpText.ActivityDone] = P(
                "done",
                "已结束战斗",
                "已結束戰鬥",
                "戦闘終了"),
            [MpText.ActivityDown] = P(
                "down",
                "已倒下",
                "已倒下",
                "戦闘不能"),
            [MpText.ActivityTurnOver] = P(
                "turn over",
                "回合已结束",
                "回合已結束",
                "ターン終了"),
            [MpText.ActivityHand] = P(
                "hand {0}",
                "手牌 {0}",
                "手牌 {0}",
                "手札 {0}"),

            // ------------------------------------------------------------------ emotes
            [MpText.EmoteNice] = P(
                "Nice!",
                "漂亮！",
                "漂亮！",
                "ナイス！"),
            [MpText.EmoteNegative] = P(
                "...",
                "……",
                "……",
                "・・・"),
            [MpText.EmoteHurryUp] = P(
                "Any day now...",
                "快点吧……",
                "快點吧……",
                "そろそろお願いします・・・"),

            // ------------------------------------------------------------------ map voting
            [MpText.MapWaitingForOne] = P(
                "Waiting for {0} to pick a node",
                "等待 {0} 选择节点",
                "等待 {0} 選擇節點",
                "{0}がノードを選択するのを待っています"),
            [MpText.MapWaitingForMany] = P(
                "Waiting for {0}",
                "等待 {0}",
                "等待 {0}",
                "{0} を待っています"),
            [MpText.MapMovingToNode] = P(
                "Moving to voted node",
                "正在前往投票选中的节点",
                "正在前往投票選中的節點",
                "投票されたノードへ移動中"),
            [MpText.MapPartySplit] = P(
                "Party is split: {0}",
                "队伍意见不一：{0}",
                "隊伍意見不一：{0}",
                "パーティーの意見が分かれています：{0}"),
            // name, x, y.
            [MpText.MapPick] = P(
                "{0} -> ({1},{2})",
                "{0} -> ({1},{2})",
                "{0} -> ({1},{2})",
                "{0} -> ({1},{2})"),
            [MpText.MapHeadingTo] = P(
                "Heading to ({0}, {1})",
                "正在前往 ({0}, {1})",
                "正在前往 ({0}, {1})",
                "移動中 ({0}, {1})"),
            [MpText.RestartPartyMoving] = P(
                "Can't restart while the party is moving to the next node. Try again once you arrive.",
                "队伍正在前往下一个节点，此时无法重来。抵达之后再试一次。",
                "隊伍正在前往下一個節點，此時無法重來。抵達之後再試一次。",
                "パーティーが次のノードへ移動中は、やり直すことができません。到着してからもう一度試してください。"),

            // ------------------------------------------------------------------ inspecting a hand
            // player name.
            [MpText.InspectBanner] = P(
                "Viewing {0}'s hand. Right-click or Esc to go back",
                "查看 {0} 的手牌中。按右键或 Esc 键以退回。",
                "查看 {0} 的手牌中。按右鍵或 Esc 鍵以退回。",
                "{0}の手札を表示中。右クリックまたはEscで戻ります。"),
			// used for inspecting a player's draw/discard/exile pile, or their library
            // player name, zone name.
            [MpText.InspectZoneTitle] = P(
                "{0} - {1}",
                "{0} - {1}",
                "{0} - {1}",
                "{0} - {1}"),

            // ------------------------------------------------------------------ session status
            [MpText.StatusHostFailed] = P(
                "Could not host: {0}",
                "无法创建房间：{0}",
                "無法建立房間：{0}",
                "ホストできませんでした：{0}"),
            [MpText.StatusHostingOnPort] = P(
                "Hosting on port {0}",
                "正在端口 {0} 上主持房间",
                "正在端口 {0} 上主持房間",
                "ポート {0}でホスト中"),
            [MpText.StatusHostSteamFailed] = P(
                "Could not host over Steam: {0}",
                "无法通过 Steam 创建房间：{0}",
                "無法透過 Steam 建立房間：{0}",
                "Steamでホストできませんでした：{0}"),
            [MpText.StatusOpeningSteamLobby] = P(
                "Opening a Steam lobby...",
                "正在创建 Steam 房间⋯⋯",
                "正在建立 Steam 房間⋯⋯",
                "Steamロビーを開いています・・・"),
            [MpText.StatusHostingOverSteam] = P(
                "Hosting over Steam. Invite friends from the overlay.",
                "正在通过 Steam 主持房间。可从 Steam 介面邀请好友。",
                "正在透過 Steam 主持房間。可從 Steam 介面邀請好友。",
                "Steamでホスト中。オーバーレイからフレンドを招待してください。"),
            [MpText.StatusConnecting] = P(
                "Connecting...",
                "正在连接⋯⋯",
                "正在連接⋯⋯",
                "接続中・・・"),
            [MpText.StatusConnectFailed] = P(
                "Could not connect: {0}",
                "无法连接：{0}",
                "無法連接：{0}",
                "接続できませんでした：{0}"),
            [MpText.StatusAlreadyInSession] = P(
                "Leave your current session before joining another",
                "加入其他房间前，请先离开当前房间",
                "加入其他房間前，請先離開當前房間",
                "別のセッションに参加する前に、現在のセッションから退出してください。"),
            [MpText.StatusConnectingSteam] = P(
                "Connecting over Steam...",
                "正在通过 Steam 连接⋯⋯",
                "正在透過 Steam 連接⋯⋯",
                "Steam経由で接続中・・・"),
            [MpText.StatusConnectSteamFailed] = P(
                "Could not connect over Steam: {0}",
                "无法通过 Steam 连接：{0}",
                "無法透過 Steam 連接：{0}",
                "Steam経由で接続できませんでした：{0}"),
            [MpText.StatusConnectedAsPlayer] = P(
                "Connected as player {0}",
                "已作为玩家 {0} 连接",
                "已作為玩家 {0} 連接",
                "プレイヤー {0}として接続しました"),
            [MpText.StatusRejected] = P(
                "Rejected: {0}",
                "已被拒绝：{0}",
                "已被拒絕：{0}",
                "拒否されました：{0}"),
            [MpText.StatusWaitingForPlayers] = P(
                "Waiting for the other players...",
                "等待其他玩家⋯⋯",
                "等待其他玩家⋯⋯",
                "他のプレイヤーを待っています・・・"),
            // One name, or several joined with commas. Unlike the map's version this reads the same
            // either way, so there is one row rather than a pair.
            [MpText.StatusWaitingForNames] = P(
                "Waiting for {0}...",
                "等待 {0}⋯⋯",
                "等待 {0}⋯⋯",
                "{0}を待っています・・・"),
            [MpText.StatusWaitingToResume] = P(
                "Ready to continue. Waiting for the rest of the party...",
                "已准备继续。等待其他玩家⋯⋯",
                "已準備繼續。等待其他玩家⋯⋯",
                "続行準備完了。他のメンバーを待っています・・・"),
            [MpText.StatusRunStarted] = P(
                "Run started (seed {0})",
                "游戏已开始（种子 {0}）",
                "遊戲已開始（種子 {0}）",
                "ラン開始（SEED {0}）"),
            [MpText.StatusRunResumed] = P(
                "Run continued (seed {0})",
                "游戏已继续（种子 {0}）",
                "遊戲已繼續（種子 {0}）",
                "ランを再開しました（SEED {0}"),
            [MpText.StatusBackInLobby] = P(
                "Back in the lobby. Start a new run, or continue this one together.",
                "已回到房间。可以开始新游戏，或一起继续这局。",
                "已回到房間。可以開始新遊戲，或一起繼續這局。",
                "ロビーに戻りました。新しいランを開始するか、このランをみんなで続けてください。"),
            [MpText.NoticeResumeStaggered] = P(
                "Not everyone saved at the same point. Whoever is behind will catch the party up.",
                "并非所有人都在同一处保存。落后的玩家会赶上大家。",
                "並非所有人都在同一處保存。落後的玩家會趕上大家。",
                "全員が同じ地点でセーブしているわけではありません。遅れているプレイヤーはパーティーに合流します。"),
            // name, reason.
            [MpText.StatusPlayerLeft] = P(
                "{0} left: {1}",
                "{0} 已离开：{1}",
                "{0} 已離開：{1}",
                "{0} が退出しました：{1}"),
            [MpText.StatusDisconnected] = P(
                "Disconnected: {0}",
                "连接已断开：{0}",
                "連接已中斷：{0}",
                "切断されました：{0}"),
            [MpText.StatusSteamLobbyFailed] = P(
                "Cannot open a Steam lobby",
                "Steam 未能创建房间，无法邀请好友",
                "Steam 未能建立房間，無法邀請好友",
                "Steamロビーを開けません。"),
            [MpText.StatusSteamJoinFailed] = P(
                "Could not join that Steam lobby",
                "无法加入该 Steam 房间",
                "無法加入該 Steam 房間",
                "Steamロビーに参加できませんでした。"),

            // ------------------------------------------------------------------ error/networking messages that are human-readable
            // host protocol, joiner protocol.
            [MpText.ReasonProtocolMismatch] = P(
                "Version mismatch: host runs protocol {0}, you run {1}, please ensure the mod is up to date",
                "版本不一致：房主使用协议 {0}，你使用协议 {1}",
                "版本不一致：房主使用協定 {0}，你使用協定 {1}",
                "バージョンが一致しません：ホストのプロトコルは{0}、あなたのプロトコルは{1}です。MODが最新バージョンになっていることを確認してください。"),
            [MpText.ReasonRunInProgress] = P(
                "The run has already started, cannot join",
                "游戏已经开始了",
                "遊戲已經開始了",
                "ランはすでに開始されているため、参加できません。"),
            [MpText.ReasonStartSplit] = P(
                "Some of the party started a new run and others continued a saved one. Everyone has to do the same thing.",
                "有人开始了新游戏，有人却在继续存档。所有人必须做同样的选择。",
                "有人開始了新遊戲，有人卻在繼續存檔。所有人必須做同樣的選擇。",
                "パーティーの一部が新しいランを開始し、他のメンバーがセーブデータを続行しました。全員が同じ選択をする必要があります。"),
            [MpText.ReasonResumeDifferentRuns] = P(
                "These saves are not the same run. Everyone has to continue the run you were playing together.",
                "这些存档不是同一局游戏。所有人都要继续你们一起玩的那一局。",
                "這些存檔不是同一局遊戲。所有人都要繼續你們一起玩的那一局。",
                "これらのセーブデータは同じランのものではありません。 全員で一緒にプレイしていたランを続行する必要があります。"),
            [MpText.ReasonSessionFull] = P(
                "Session is full, cannot join",
                "房间已满",
                "房間已滿",
                "セッションが満員のため、参加できません。"),
            [MpText.ReasonYouLeft] = P(
                "You left the session",
                "你离开了房间",
                "你離開了房間",
                "セッションから退出しました。"),
            [MpText.ReasonRemoteClosed] = P(
                "Remote closed the connection",
                "对方关闭了连接",
                "對方關閉了連接",
                "相手が接続を切断しました。"),
            [MpText.ReasonConnectionFailed] = P(
                "Connection failed",
                "连接失败",
                "連接失敗",
                "接続に失敗しました。"),
            [MpText.ReasonSteamClosed] = P(
                "Steam connection closed",
                "Steam 连接已关闭",
                "Steam 連接已關閉",
                "Steamとの接続が切断されました。"),
            [MpText.ReasonReadFailed] = P(
                "Read failed: {0}",
                "读取失败：{0}",
                "讀取失敗：{0}",
                "読み込みに失敗しました：{0}"),
            [MpText.ReasonWriteFailed] = P(
                "Write failed: {0}",
                "写入失败：{0}",
                "寫入失敗：{0}",
                "書き込みに失敗しました：{0}"),
            [MpText.ReasonSendFailed] = P(
                "Send failed: {0}",
                "发送失败：{0}",
                "傳送失敗：{0}",
                "送信に失敗しました：{0}"),
            // address, port.
            [MpText.ReasonTimedOut] = P(
                "Timed out connecting to {0}:{1}",
                "连接 {0}：{1} 超时",
                "連接 {0}：{1} 逾時",
                "{0}:{1}への接続がタイムアウトしました。"),

            // ------------------------------------------------------------------ transports
            [MpText.ErrorSteamUnavailable] = P(
                "Steam is not available",
                "Steam 不可用",
                "Steam 無法使用",
                "Steamを利用できません。"),
            [MpText.ErrorSteamListenFailed] = P(
                "Steam refused to open a listen socket",
                "Steam 拒绝开启监听端口",
                "Steam 拒絕開啟監聽端口",
                "Steamがリッスンソケットの開放を拒否しました。"),
            [MpText.ErrorSteamConnectFailed] = P(
                "Steam refused to open a connection",
                "Steam 拒绝建立连接",
                "Steam 拒絕建立連接",
                "Steamが接続の確立を拒否しました。")
        };
    }
}
