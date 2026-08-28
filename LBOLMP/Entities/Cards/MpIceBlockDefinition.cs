using System.Collections.Generic;
using LBOLMP.Entities.StatusEffects;
using LBOLMP.Net;
using LBOLMP.Session.Battle;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Cards;
using LBoL.Core.StatusEffects;
using LBoL.EntityLib.Cards.Character.Cirno;
using LBoL.EntityLib.StatusEffects.Cirno;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;
using LBoLEntitySideloader.Resource;

namespace LBOLMP.Entities.Cards
{
    /// <summary>
    /// Multiplayer version of Cirno's Ice Block, which it replaces.
    /// Plays the singleplayer Ice Block on a person of your choice (including yourself).
    /// </summary>
    public sealed class MpIceBlockDefinition : LbolMpMultiplayerCardTemplate<MpProxyCardPayload>
    {
        public override IdContainer GetId() => nameof(MpIceBlock);

        /// <summary>Borrows the vanilla card's art.</summary>
        public override CardImages LoadCardImages() => BorrowVanillaArt(nameof(IceBlock));

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Skill;
            config.Rarity = Rarity.Rare;
            config.Owner = VanillaCharNames.Cirno;
            config.Colors = new List<ManaColor> { ManaColor.Blue };
            config.Cost = new ManaGroup { Any = 1, Blue = 2 };
            config.UpgradedCost = new ManaGroup { Blue = 1 };
            config.Keywords = Keyword.Exile;
            config.UpgradedKeywords = Keyword.Exile;
            config.ImageId = nameof(IceBlock);

            // Set to TargetType.SingleEnemy just so we can borrow the selector logic.
            // PartyTargetPatches points it at the party instead of enemies.
            config.TargetType = TargetType.SingleEnemy;

            config.RelativeEffects = new List<string> { nameof(MpPartner), nameof(Immune) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner), nameof(Immune) };
            config.RelativeKeyword = Keyword.Block | Keyword.Shield | Keyword.Retain;
            config.UpgradedRelativeKeyword = Keyword.Block | Keyword.Shield | Keyword.Retain;
            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpProxyCardPayload payload, BattleController battle, int senderId)
            => payload.Play(senderId);
    }

    [EntityLogic(typeof(MpIceBlockDefinition))]
    public sealed class MpIceBlock : Card, IMpAnyPlayerTargeted
    {
        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            int target = MpPartyTargeting.Consume();
            var payload = new MpProxyCardPayload { CardId = nameof(IceBlock), Upgraded = IsUpgraded };

            // If we aim this at ourselves, just play the thing
            if (target == MpNet.LocalPlayerId)
            {
                foreach (var action in payload.Play(MpNet.LocalPlayerId))
                {
                    yield return action;
                }

                yield break;
            }

            // Otherwise, play it on whatever partner we selected
            MpEffects.Send(Id, payload, MpEffectTarget.Partner, target);
        }
    }
}
