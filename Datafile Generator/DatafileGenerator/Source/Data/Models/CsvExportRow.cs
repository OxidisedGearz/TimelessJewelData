using DatafileGenerator.Data.Models;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace DatafileGenerator.Source.Data.Models
{
    public class CsvExportRow
    {
        public int JewelSeed { get; set; }
        public int JewelSocketId { get; set; }

        [IgnoreDataMember]
        public int JewelType { get; set; }
        [IgnoreDataMember]
        public string JewelName { get; set; }

        [IgnoreDataMember]
        public uint NotableId { get; set; }
        [IgnoreDataMember]
        public string NotableName { get; set; }

        [IgnoreDataMember]
        public uint NotableReplacementIndex { get; set; }
        public string NotableReplacementName { get; set; }
        
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
            NotableId = notable.GraphIdentifier;
            NotableReplacementName = notableJewelReplacementName;
            NotableReplacementIndex = notableIndex;
        }
    }
}
