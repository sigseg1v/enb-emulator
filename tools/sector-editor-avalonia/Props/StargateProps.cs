using System;
using System.ComponentModel;

using N7.Utilities;

namespace N7.Props
{
    class StargateProps : BaseProps
    {
        private bool is_class_specific;
        private String faction_id;

        public StargateProps() { }

        public void fillBaseProps(BaseProps bp)
        {
            this.SectorID = bp.SectorID;
            this.NavType = bp.NavType;
            this.Signature = bp.Signature;
            this.IsHuge = bp.IsHuge;
            this.BaseXP = bp.BaseXP;
            this.ExplorationRange = bp.ExplorationRange;
            this.BaseAssetID = bp.BaseAssetID;
            this.Color = bp.Color;
            this.Type = bp.Type;
            this.Scale = bp.Scale;
            this.PositionX = bp.PositionX;
            this.PositionY = bp.PositionY;
            this.PositionZ = bp.PositionZ;
            this.Name = bp.Name;
            this.AppearsInRadar = bp.AppearsInRadar;
            this.RadarRange = bp.RadarRange;
            this.Destination = bp.Destination;
        }

        [CategoryAttribute("Stargate Object Props"), DescriptionAttribute("Is this stargate limited to a certain class ?")]
        public bool IsClassSpecific { get { return is_class_specific; } set { is_class_specific = value; } }

        [CategoryAttribute("Stargate Object Props"), DescriptionAttribute("The faction ID of the faction that controls this stargate")]
        [Browsable(true)]
        public string FactionID
        {
            get
            {
                if (faction_id != null) return faction_id;
                if (HE_GlobalVars._ListofFactions != null && HE_GlobalVars._ListofFactions.Length > 0)
                {
                    Array.Sort(HE_GlobalVars._ListofFactions);
                    return HE_GlobalVars._ListofFactions[0];
                }
                return "";
            }
            set { faction_id = value; }
        }
    }
}
