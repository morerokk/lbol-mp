using System.Collections.Generic;
using System.Linq;
using LBOLMP.Entities.StatusEffects;
using LBOLMP.Net;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Cards;
using LBoL.Core.Randoms;
using LBoL.EntityLib.Cards.Character.Cirno.Friend;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;

namespace LBOLMP.Entities.Cards.Cirno
{
    public sealed class MpInspiredBondsFairyPayload : MpEffectPayload
    {
    }

    public sealed class MpInspiredBondsDefinition : LbolMpMultiplayerCardTemplate<MpInspiredBondsFairyPayload>
    {
        private static readonly HashSet<string> FairiesOfLight = new HashSet<string>
        {
            nameof(SunnyFriend), nameof(LunaFriend), nameof(StarFriend), nameof(ClownpieceFriend)
        };

        internal static readonly ManaGroup FairyCost = ManaGroup.Empty;

        public override IdContainer GetId() => nameof(MpInspiredBonds);

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Ability;
            config.Rarity = Rarity.Rare;
            config.Owner = VanillaCharNames.Cirno;
            config.Colors = new List<ManaColor> { ManaColor.Green };
            config.Cost = new ManaGroup { Green = 2 };
            config.UpgradedCost = ManaGroup.Empty;
            config.TargetType = TargetType.Self;

            // How much Unity a partner's teammate gets each time.
            config.Value1 = 1;

            // The mana tooltip, and the cost the fairy has in the partner's hand.
            config.Mana = FairyCost;

            config.RelativeEffects = new List<string> { nameof(MpPartner) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner) };
            config.RelativeKeyword =
                Keyword.FriendCard | Keyword.Loyalty | Keyword.TempMorph | Keyword.Ethereal;
            config.UpgradedRelativeKeyword = config.RelativeKeyword;

            config.Illustrator = "";

            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpInspiredBondsFairyPayload payload, BattleController battle, int senderId)
        {
            if (battle.BattleShouldEnd)
            {
                yield break;
            }

            // Roll a random Fairy of Light
            var fairy = battle.RollCardsWithoutManaLimit(
                new CardWeightTable(RarityWeightTable.BattleCard, OwnerWeightTable.AllOnes,
                    CardTypeWeightTable.OnlyFriend, false),
                1,
                config => FairiesOfLight.Contains(config.Id)).FirstOrDefault();

            if (fairy == null)
            {
                yield break;
            }

            fairy.SetTurnCost(FairyCost);
            fairy.IsEthereal = true;

            yield return new AddCardsToHandAction(new[] { fairy });
        }
    }

    [EntityLogic(typeof(MpInspiredBondsDefinition))]
    public sealed class MpInspiredBonds : Card
    {
        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            yield return BuffAction<MpInspiredBondsSe>(Value1, 0, 0, 0, 0.2f);

            MpEffects.Send(Id, new MpInspiredBondsFairyPayload(), MpEffectTarget.AllPartners);
        }
    }
}
