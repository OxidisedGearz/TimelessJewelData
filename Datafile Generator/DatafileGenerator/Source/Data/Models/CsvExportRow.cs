using DatafileGenerator.Data.Models;
using System.Collections.Generic;

namespace DatafileGenerator.Source.Data.Models
{
    public class CsvExportRow
    {
        public int JewelSeed { get; set; }
        public int JewelType { get; set; }
        public string JewelName { get; set; }
        public int JewelSocketId { get; set; }

        public string NotableName { get; set; }
        public string NotableReplacementName { get; set; }
        public uint NotableReplacementIndex { get; set; }

        public CsvExportRow(
            int jewelSeed,
            string jewelName,
            int jewelType,
            PassiveSkill notable,
            uint notableIndex,
            string notableJewelReplacementName,
            Dictionary<string, int> notableJewelSocketMappings
        )
        {
            JewelSeed = jewelSeed;
            JewelType = jewelType;
            JewelName = jewelName;
            JewelSocketId = notableJewelSocketMappings.ContainsKey(notable.Name) ? notableJewelSocketMappings[notable.Name] : 0;

            NotableName = notable.Name;
            NotableReplacementName = notableJewelReplacementName;
            NotableReplacementIndex = notableIndex;
        }
    }
}
