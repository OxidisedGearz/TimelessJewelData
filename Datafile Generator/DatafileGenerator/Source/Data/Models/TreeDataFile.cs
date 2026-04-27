using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DatafileGenerator.Data.Models
{
    public class SkillGroup
    {
        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        [JsonPropertyName("orbits")]
        public int[] Orbits { get; set; }

        [JsonPropertyName("nodes")]
        public string[] Nodes { get; set; }
    }

    public class SkillTreeConstants
    {
        [JsonPropertyName("skillsPerOrbit")]
        public int[] SkillsPerOrbit { get; set; }

        [JsonPropertyName("orbitRadii")]
        public int[] OrbitRadii { get; set; }
    }

    public class TreeDataFile
    {
        [JsonPropertyName("groups")]
        public Dictionary<int, SkillGroup> Groups { get; set; }

        [JsonPropertyName("nodes")]
        public Dictionary<string, PassiveSkill> PassiveSkills { get; set; }

        [JsonPropertyName("constants")]
        public SkillTreeConstants Constants { get; set; }

        [JsonPropertyName("min_x")]
        public double MinX { get; set; }

        [JsonPropertyName("min_y")]
        public double MinY { get; set; }
    }
}
