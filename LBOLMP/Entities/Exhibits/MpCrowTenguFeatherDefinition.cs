using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Units;
using LBoL.EntityLib.Exhibits.Common;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;
using LBoLEntitySideloader.Resource;

namespace LBOLMP.Entities.Exhibits
{
    /// <summary>
    /// The map half of Crow Tengu's Wing (<see cref="TiangouYuyi"/>), handed to everyone else when one player picks the real one up.
    /// Otherwise the party could never agree on a node off their path.
    /// </summary>
    public sealed class MpCrowTenguFeatherDefinition : ExhibitTemplate
    {
        public override IdContainer GetId() => nameof(MpCrowTenguFeather);

        public override LocalizationOption LoadLocalization() => MpLocalization.Exhibits.AddEntity(this);

        // Borrows Crow Tengu's Wing's icon through IconName instead.
        public override ExhibitSprites LoadSprite() => null;

        public override ExhibitConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.IsPooled = false;
            config.Appearance = AppearanceType.Nowhere;
            config.Rarity = Rarity.Common;
            config.HasCounter = true;
            config.InitialCounter = ChargesPerWing;

            // How many charges each extra wing adds.
            config.Value1 = ChargesPerWing;

            return config;
        }

        /// <summary>Crow Tengu's Wing's own charges.</summary>
        public const int ChargesPerWing = 3;
    }

    [EntityLogic(typeof(MpCrowTenguFeatherDefinition))]
    public sealed class MpCrowTenguFeather : Exhibit, IMapModeOverrider
    {
        public override string IconName => nameof(TiangouYuyi);

        protected override void OnAdded(PlayerUnit player)
        {
            GameRun.AddMapModeOverrider(this);
        }

        protected override void OnRemoved(PlayerUnit player)
        {
            GameRun.RemoveMapModeOverrider(this);
        }

        public GameRunMapMode? MapMode => Counter > 0 ? GameRunMapMode.Crossing : (GameRunMapMode?)null;

        public void OnEnteredWithMode()
        {
            Counter -= 1;
            NotifyActivating();
        }

        /// <summary>Another wing was picked up in the party.</summary>
        public void AddCharges(int charges)
        {
            Counter += charges;
            NotifyActivating();

            // The map only asks the overriders again when something changes, so tell it.
            GameRun?.CheckMapMode();
        }
    }
}
