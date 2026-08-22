using System.Collections.Generic;
using LBOLMP.Net;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.StatusEffects;
using LBoL.Core.Units;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;
using LBoLEntitySideloader.Resource;
using UnityEngine;

namespace LBOLMP.Entities
{
    /// <summary>Which card the partners are being asked to play.</summary>
    public sealed class MpProxyCardPayload : MpEffectPayload
    {
        public string CardId;
        public bool Upgraded;
    }

    /// <summary>
    /// The next Ability card you play is also played for all your Partners.
    /// </summary>
    public sealed class MpOfferingSeDefinition : MpStatusEffectTemplate<MpProxyCardPayload>
    {
        private static DirectorySource _source;

        private static DirectorySource Source => _source ?? (_source = new DirectorySource(MpInfo.Guid, ""));

        public override IdContainer GetId() => nameof(MpOfferingSe);

        public override LocalizationOption LoadLocalization()
        {
            var files = new LocalizationFiles(Source, Locale.En);
            files.AddLocaleFile(Locale.En, "Resources/StatusEffectsEn.yaml");
            files.AddLocaleFile(Locale.ZhHans, "Resources/StatusEffectsZhHans.yaml");
            files.AddLocaleFile(Locale.ZhHant, "Resources/StatusEffectsZhHant.yaml");
            files.AddLocaleFile(Locale.Ja, "Resources/StatusEffectsJa.yaml");
            return files;
        }

        /// <summary>
        /// Falls back to the Resilient icon until Resources/MpOfferingSe.png exists, so the status
        /// HUD always has something to draw. Drop the fallback once the real art lands.
        /// </summary>
        public override Sprite LoadSprite() =>
            ResourceLoader.LoadSprite("Resources/MpOfferingSe.png", Source)
            ?? ResourceLoader.LoadSprite("Resources/MpResilient.png", Source);

        public override StatusEffectConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = StatusEffectType.Positive;
            config.HasLevel = false;
            config.HasDuration = false;
            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpProxyCardPayload payload, BattleController battle, int senderId)
        {
            var copy = Library.TryCreateCard(payload.CardId, payload.Upgraded);
            if (copy == null)
            {
                MpPlugin.Log.LogWarning($"Cannot play '{payload.CardId}' for player {senderId}; unknown card");
                yield break;
            }

            // Vanilla's marker for a card that was conjured for one play and should not stick
            // around afterwards. FollowAttackAction does exactly this with its random filler cards.
            // Without it the copy would land in our discard pile and stay in the deck for the fight.
            copy.IsPlayTwiceToken = true;

            yield return new PlayCardAction(copy);
        }
    }

    /// <summary>
    /// The next Ability card you play is also played for all your Partners.
    /// </summary>
    [EntityLogic(typeof(MpOfferingSeDefinition))]
    public sealed class MpOfferingSe : StatusEffect
    {
        protected override void OnAdded(Unit unit)
        {
            // CardUsed, never CardPlayed. CardUsed only fires when a player actually plays a card
            // from their hand; CardPlayed also fires for free plays and for the copies we ask
            // partners to play. Listening on CardPlayed would let two of these bounce off each
            // other forever.
            ReactOwnerEvent(Battle.CardUsed, new EventSequencedReactor<CardUsingEventArgs>(OnCardUsed));
        }

        private IEnumerable<BattleAction> OnCardUsed(CardUsingEventArgs args)
        {
            if (args.Card == null || args.Card.CardType != CardType.Ability || Battle.BattleShouldEnd)
            {
                yield break;
            }

            NotifyActivating();
            args.AddModifier(this);

            MpEffects.Send(Id,
                new MpProxyCardPayload { CardId = args.Card.Id, Upgraded = args.Card.IsUpgraded },
                MpEffectTarget.AllPartners);

            yield return new RemoveStatusEffectAction(this);
        }
    }
}
