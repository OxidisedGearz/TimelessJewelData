using DatafileGenerator.Data.Models;

namespace DatafileGenerator.Source.Data.Models
{
    public class CsvExportRow
    {
        public int JewelSeed { get; set; }
        public int JewelType { get; set; }
        public string JewelName { get; set; }

        public string NotableName { get; set; }

        public string NotableReplacementName { get; set; }

        public uint? Orbit { get; set; }

        public uint NotableIndex { get; set; }

        public byte PassiveSkillIndex { get; set; }

        public CsvExportRow(int jewelSeed, string jewelName, int jewelType, PassiveSkill notable, uint notableIndex, AlternatePassiveSkill notableJewelReplacement)
        {
            JewelSeed = jewelSeed;
            JewelName = jewelName;
            JewelType = jewelType;
            AddNotableMapping(notable, notableIndex, notableJewelReplacement);
        }

        public void AddNotableMapping(PassiveSkill notable, uint notableIndex, AlternatePassiveSkill notableJewelReplacement)
        {
            NotableName = notable.Name;
            NotableReplacementName = notableJewelReplacement.Name;
            Orbit = notable.Orbit;
            NotableIndex = notableIndex;
        }
    }
}
