using System;
using System.ComponentModel;

using N7.Utilities;

namespace N7.Props
{
    class HarvestableProps : BaseProps
    {
        private String level;
        private String field_value;       // `field` is a reserved accessor keyword in C# 14
        private String res_type;
        private int res_count;
        private float spawn_radius;
        private int pop_rock_chance;
        private String spawn_group_id;
        private float max_field_radius;

        public HarvestableProps() { }

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

        [CategoryAttribute("Harvestable Object Props"), DescriptionAttribute("The average level of the resource field")]
        [Browsable(true)]
        public string Level
        {
            get
            {
                if (level != null) return level;
                if (HE_GlobalVars._ListofLevels.Length > 0)
                {
                    Array.Sort(HE_GlobalVars._ListofLevels);
                    return HE_GlobalVars._ListofLevels[0];
                }
                return "";
            }
            set { level = value; }
        }

        [CategoryAttribute("Harvestable Object Props"), DescriptionAttribute("The type of resource field")]
        [Browsable(true)]
        public string Field
        {
            get
            {
                if (field_value != null) return field_value;
                if (HE_GlobalVars._ListofFieldTypes.Length > 0)
                    return HE_GlobalVars._ListofFieldTypes[0];
                return "";
            }
            set { field_value = value; }
        }

        [CategoryAttribute("Harvestable Object Props"), DescriptionAttribute("The number of harvestable(resource) objects in this field")]
        public int ResCount { get { return res_count; } set { res_count = value; } }

        [CategoryAttribute("Harvestable Object Props"), DescriptionAttribute("The types of harvestables(resources) in this field, collection object")]
        public String ResType { get { return res_type; } set { res_type = value; } }

        [CategoryAttribute("Harvestable Object Props"), DescriptionAttribute("The maximum radius at which the field spawns resources.")]
        public float MaxFieldRadius { get { return max_field_radius; } set { max_field_radius = value; } }

        [CategoryAttribute("Harvestable Object Props"), DescriptionAttribute("If the field contains mob guardians, the radius at which they will spread out too. Radius should be no larger then the Maximum field size.")]
        public float MobSpawnRadius { get { return spawn_radius; } set { spawn_radius = value; } }

        [CategoryAttribute("Harvestable Object Props"), DescriptionAttribute("The percentage chance that the field will contain pop-rocks. (1-100)")]
        public int PopRockChance { get { return pop_rock_chance; } set { pop_rock_chance = value; } }

        [CategoryAttribute("Harvestable Object Props"), DescriptionAttribute("The mob spawn group that guard this field, collection object")]
        public String SpawnGroup { get { return spawn_group_id; } set { spawn_group_id = value; } }
    }
}
