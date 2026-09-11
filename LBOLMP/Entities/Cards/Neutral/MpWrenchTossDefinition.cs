using System.Collections.Generic;
using System.Linq;
using LBOLMP.Entities.Guns;
using LBOLMP.Entities.StatusEffects;
using LBOLMP.Net;
using LBoL.Base;
using LBoL.Base.Extensions;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Cards;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoL.EntityLib.EnemyUnits.Normal;

namespace LBOLMP.Entities.Cards.Neutral
{
    /// <summary>The wrench in flight: how hard it hits by now, and whether it was upgraded.</summary>
    public sealed class MpWrenchTossPayload : MpEffectPayload
    {
        public int DeltaDamage;
        public bool Upgraded;
    }

    /// <summary>
    /// A wrench that hits harder every time somebody throws it, and never lands in the same
    /// deck twice in a row.
    /// </summary>
    public sealed class MpWrenchTossDefinition : LbolMpMultiplayerCardTemplate<MpWrenchTossPayload>
    {
        private const string GunBorrowedFrom = nameof(HetongYinchen);
        private const string MirroredGun = "MpWrenchTossGun";
        private const string FallbackGun = "Empty";

        /// <summary>
        /// What the card costs after it has been passed to a partner.
        /// </summary>
        internal static readonly ManaGroup PassedCost = new ManaGroup { Any = 2 };

        public override IdContainer GetId() => nameof(MpWrenchToss);

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Attack;
            config.Rarity = Rarity.Uncommon;
            config.Colors = new List<ManaColor> { ManaColor.Blue };
            config.Cost = new ManaGroup { Any = 1, Blue = 1 };
            config.UpgradedCost = PassedCost;
            config.TargetType = TargetType.SingleEnemy;
            config.Keywords = Keyword.Exile;
            config.UpgradedKeywords = Keyword.Exile;

            config.Damage = 10;

            // How much more damage it does every time it's used.
            config.Value1 = 5;
            config.UpgradedValue1 = 7;

            config.GunName = BorrowedGun;

            config.RelativeEffects = new List<string> { nameof(MpPartner) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner) };

            config.Illustrator = "みず";

            return config;
        }

        private static string BorrowedGun =>
            MpMirroredGuns.OfEnemy(GunBorrowedFrom, MirroredGun) ?? FallbackGun;

        public override IEnumerable<BattleAction> Receive(
            MpWrenchTossPayload payload, BattleController battle, int senderId)
        {
            if (battle.BattleShouldEnd)
            {
                yield break;
            }

            var thrown = Library.CreateCard(typeof(MpWrenchToss), payload.Upgraded);
            thrown.DeltaDamage = payload.DeltaDamage;
            thrown.SetBaseCost(PassedCost);

            yield return new AddCardsToDrawZoneAction(
                new[] { thrown }, DrawZoneTarget.Top, AddCardsType.Normal);
        }
    }

    [EntityLogic(typeof(MpWrenchTossDefinition))]
    public sealed class MpWrenchToss : Card
    {
        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            yield return AttackAction(selector);

            DeltaDamage += Value1;

            var partners = MpEffects.ValidPartners.ToList();
            if (partners.Count == 0)
            {
                yield break;
            }

            MpEffects.Send(
                Id,
                new MpWrenchTossPayload { DeltaDamage = DeltaDamage, Upgraded = IsUpgraded },
                MpEffectTarget.Partner,
                partners.Sample(BattleRng).PlayerId);
        }
    }
}
