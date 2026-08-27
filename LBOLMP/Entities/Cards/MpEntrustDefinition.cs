using System.Collections.Generic;
using System.Linq;
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
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;
using LBoLEntitySideloader.Resource;

namespace LBOLMP.Entities.Cards
{
    public sealed class MpEntrustPayload : MpEffectPayload
    {
        public int Firepower;
        public int TempFirepower;
        public int FirepowerDown;
        public int TempFirepowerDown;
    }

    public sealed class MpEntrustDefinition : MpCardTemplate<MpEntrustPayload>
    {
        private static DirectorySource _source;

        private static DirectorySource Source => _source ?? (_source = new DirectorySource(MpInfo.Guid, ""));

        public override IdContainer GetId() => nameof(MpEntrust);

        public override LocalizationOption LoadLocalization() => MpLocalization.Cards.AddEntity(this);

        public override CardImages LoadCardImages()
        {
            var images = new CardImages(Source);
            images.AutoLoad(this, extension: ".png", relativePath: "Resources/Cards/");
            return images;
        }

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Skill;
            config.Rarity = Rarity.Uncommon;
            config.Owner = VanillaCharNames.Reimu;
            config.Colors = new List<ManaColor> { ManaColor.Red };
            config.Cost = new ManaGroup { Red = 1 };
            config.Keywords = Keyword.Exile | Keyword.Retain;
            config.UpgradedKeywords = Keyword.Retain;

            // Set to TargetType.SingleEnemy just so we can borrow the selector logic.
            // PartyTargetPatches points it at a partner instead of enemies.
            config.TargetType = TargetType.SingleEnemy;

            config.RelativeEffects = new List<string>
            {
                nameof(MpPartner), nameof(Firepower), nameof(TempFirepower),
                nameof(FirepowerNegative), nameof(TempFirepowerNegative)
            };
            config.UpgradedRelativeEffects = new List<string>
            {
                nameof(MpPartner), nameof(Firepower), nameof(TempFirepower),
                nameof(FirepowerNegative), nameof(TempFirepowerNegative)
            };
            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpEntrustPayload payload, BattleController battle, int senderId)
        {
            if (battle.BattleShouldEnd)
            {
                yield break;
            }

            if (payload.Firepower > 0)
            {
                yield return new ApplyStatusEffectAction<Firepower>(
                    battle.Player, payload.Firepower, occupationTime: 0.2f);
            }

            if (payload.TempFirepower > 0)
            {
                yield return new ApplyStatusEffectAction<TempFirepower>(
                    battle.Player, payload.TempFirepower, occupationTime: 0.2f);
            }

            // Lol get firepower downed idiot
            if (payload.FirepowerDown > 0)
            {
                yield return new ApplyStatusEffectAction<FirepowerNegative>(
                    battle.Player, payload.FirepowerDown, occupationTime: 0.2f);
            }

            if (payload.TempFirepowerDown > 0)
            {
                yield return new ApplyStatusEffectAction<TempFirepowerNegative>(
                    battle.Player, payload.TempFirepowerDown, occupationTime: 0.2f);
            }
        }
    }

    [EntityLogic(typeof(MpEntrustDefinition))]
    public sealed class MpEntrust : Card, IMpPartnerTargeted
    {
        public override bool CanUse => MpPartyTargeting.AnyValidPartner;

        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            int partner = MpPartyTargeting.Consume();

            var moving = new StatusEffect[]
            {
                Battle.Player.GetStatusEffect<Firepower>(),
                Battle.Player.GetStatusEffect<TempFirepower>(),
                Battle.Player.GetStatusEffect<FirepowerNegative>(),
                Battle.Player.GetStatusEffect<TempFirepowerNegative>()
            };

            var payload = new MpEntrustPayload
            {
                Firepower = moving[0]?.Level ?? 0,
                TempFirepower = moving[1]?.Level ?? 0,
                FirepowerDown = moving[2]?.Level ?? 0,
                TempFirepowerDown = moving[3]?.Level ?? 0
            };

            if (moving.All(effect => effect == null))
            {
                yield break;
            }

            foreach (var effect in moving)
            {
                if (effect != null)
                {
                    yield return new RemoveStatusEffectAction(effect, true, 0.1f);
                }
            }

            MpEffects.Send(Id, payload, MpEffectTarget.Partner, partner);
        }
    }
}
