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
    /// Just the "travel to any node" part of Crow Tengu's Wing (<see cref="TiangouYuyi"/>), given to everyone else when one player obtains Crow Tengu's Wing.
    /// </summary>
    public sealed class MpCrowTenguFeatherDefinition : ExhibitTemplate
    {
        public const int ChargesPerWing = 3;

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

        public void AddCharges(int charges)
        {
            Counter += charges;
            NotifyActivating();
            GameRun?.CheckMapMode();
        }
    }
}
