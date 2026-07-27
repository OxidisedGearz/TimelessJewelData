using DatafileGenerator.Data;
using DatafileGenerator.Data.Models;
using DatafileGenerator.Game;
using DatafileGenerator.Source.Data.Models;
using ServiceStack;
using ServiceStack.Text;
using Spectre.Console;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatafileGenerator;

public static class Program
{

    private const int MightOfTheVaal = 76;
    private const int LegacyOfTheVaal = 77;
    private const int MaxBytesInFile = 5242880; //5MB
    private const string LuaMappingFileName = "NodeIndexMapping";
    private const string LuaMappingFileType = ".lua";
    private const string CsvFileName = "node_indices.csv";

    public static void Main()
    {
        Console.Title = $"{GeneratorSettings.ApplicationName} (v{GeneratorSettings.ApplicationVersion})";
        //prompt
        AnsiConsole.MarkupLine("[green]Spinning up[/]!");
        // Load input files
        GeneratorSettings.AlternatePassiveAdditionsFilePath = Path.GetFullPath(@"source-data\alternatepassiveadditions.json");
        if(!File.Exists(GeneratorSettings.AlternatePassiveAdditionsFilePath)){
            PromptUserForFile("Path to [yellow]alternate passive ADDITIONS[/] file:", out GeneratorSettings.AlternatePassiveAdditionsFilePath);
        }
        GeneratorSettings.AlternatePassiveSkillsFilePath = Path.GetFullPath(@"source-data\alternatepassiveskills.json");
        if(!File.Exists(GeneratorSettings.AlternatePassiveSkillsFilePath)){
            PromptUserForFile("Path to [yellow]alternate passive SKILLS[/] file:", out GeneratorSettings.AlternatePassiveSkillsFilePath);
        }
        GeneratorSettings.PassiveSkillsFilePath = Path.GetFullPath(@"source-data\data.json");
        if(!File.Exists(GeneratorSettings.PassiveSkillsFilePath)){
            PromptUserForFile("Path to [yellow]skill tree[/] file:", out GeneratorSettings.PassiveSkillsFilePath);
        }
        var outputDir = Path.GetFullPath(@"output-data");
        if(!Directory.Exists(outputDir)){
            PromptUserForFile("Path to [yellow]output[/] directory:", out outputDir, true);
        }
        
        const string OutputCompressedFiles = "compressed";
        const string OutputUncompressedFiles = "uncompressed";
        const string OutputBothFileFormats = "both";
        const string OutputCsvFiles = "csv";
        const string OutputCsvFilesCompressed = "csv (compressed)";
        var exportOptions = new List<string>() { OutputCompressedFiles, OutputUncompressedFiles, OutputBothFileFormats, OutputCsvFiles, OutputCsvFilesCompressed };
        PromptUserForChoice("Select output type:", exportOptions, out int compressionChoice);
        var compression = exportOptions.ElementAt(compressionChoice);
        var isCsvExport = compression == OutputCsvFiles || compression == OutputCsvFilesCompressed;
        var compressCSVOutput = compression == OutputCsvFilesCompressed;

        AnsiConsole.MarkupLine("[green]Loading[/]...");

        if (!DataManager.Initialize())
            ExitWithError("Failed to initialize the [yellow]data manager[/].");
        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        Dictionary<string, int> notableJewelSocketMappings = null;
        if (isCsvExport)
        {
            AnsiConsole.MarkupLine("[green]Calculating Notable skills affected by jewel radii[/]...");
            var calculator = new AffectedNotablesCalculator();
            notableJewelSocketMappings = calculator.GetNotableToSocketMapping();
            AnsiConsole.MarkupLine($"{notableJewelSocketMappings.Count} Affected Notable skills found");
        }

        var justNotables = GetModifiableNodes(true);
        var justSmallNodes = GetModifiableNodes(false);
        var justAscendancies = GetAscendancyNotables();
        justNotables.Sort();
        justSmallNodes.Sort();
        justAscendancies.Sort();
        //generate our indices
        var notablesThenSmalls = justNotables.ToList();
        notablesThenSmalls.AddRange(justSmallNodes);
        var allNodes = notablesThenSmalls.ToList();
        allNodes.AddRange(justAscendancies);

        AnsiConsole.MarkupLine("[green]Processing[/]...");
        //build the csv
        if (File.Exists(CsvFileName))
            File.Delete(CsvFileName);
        var sb = new StringBuilder("PassiveSkillGraphId,Name,Datafile Parsing Index\n");
        for (int k = 0; k < allNodes.Count; k++)
        {
            var node = allNodes[k];
            sb.AppendLine(node.GraphIdentifier + "," + (node.Name.Contains(',') ? ("\"" + node.Name + "\"") : node.Name) + "," + k);
        }
        File.WriteAllText(Path.Combine(outputDir, CsvFileName), sb.ToString());
        sb.Clear();
        //begin iterating over the different jewel types
        //reverse order since glorious vanity sucks
        string outputPath = null;
        List<CsvExportRow> csvExport;
        for (int i = 11; i > 0; i--)
        {
            var sw = Stopwatch.StartNew();
            GetJewelTypeInfo(i, out _, out _, out _, out string jewelName);
            AnsiConsole.MarkupLine($"[green]Calculating {jewelName}[/]...");
            byte[] dataBuffer;
            //glorious vanity logic
            if (i == 1)
            {
                //calculate
                GenerateGloriousVanity(notablesThenSmalls, isCsvExport, notableJewelSocketMappings, out var luaSizes, out dataBuffer, out csvExport);
                //create the lua mapping file
                sb.Clear();
                sb.AppendLine("nodeIDList = { }");
                sb.AppendLine($"nodeIDList[\"size\"] = {notablesThenSmalls.Count}");
                sb.AppendLine($"nodeIDList[\"sizeNotable\"] = {justNotables.Count}");
                for (int k = 0; k < notablesThenSmalls.Count; k++)
                {
                    var node = notablesThenSmalls[k];
                    sb.AppendLine($"nodeIDList[{node.GraphIdentifier}] = {{ index = {k}, size = {luaSizes[k]} }}");
                }
                sb.Append("return nodeIDList");
                File.WriteAllText(Path.Combine(outputDir, LuaMappingFileName + jewelName + LuaMappingFileType), sb.ToString());
                sb.Clear();
            }
            //abyssal logic
            else if (i>=7 && i<=11)
            {
                var nodesInPlay = i == 11 ? allNodes : notablesThenSmalls;
                GenerateAbyssal(nodesInPlay, isCsvExport, i, notableJewelSocketMappings, out var luaSizes, out dataBuffer, out csvExport);
                //create the lua mapping file
                sb.Clear();
                sb.AppendLine("nodeIDList = { }");
                sb.AppendLine($"nodeIDList[\"size\"] = {nodesInPlay.Count}");
                sb.AppendLine($"nodeIDList[\"sizeNotable\"] = {justNotables.Count}");
                if (i==11)
                {
                    sb.AppendLine($"nodeIDList[\"sizeSmall\"] = {justSmallNodes.Count}");
                }
                for (int k = 0; k < nodesInPlay.Count; k++)
                {
                    var node = nodesInPlay[k];
                    sb.AppendLine($"nodeIDList[{node.GraphIdentifier}] = {{ index = {k}, size = {luaSizes[k]} }}");
                }
                sb.Append("return nodeIDList");
                File.WriteAllText(Path.Combine(outputDir, LuaMappingFileName + jewelName + LuaMappingFileType), sb.ToString());
                sb.Clear();
            }
            //standard logic
            else
            {
                GenerateRegular(justNotables, isCsvExport, i, notableJewelSocketMappings, out dataBuffer, out csvExport);
            }
            //output uncompressed
            if (compression == OutputUncompressedFiles || compression == OutputBothFileFormats)
            {
                AnsiConsole.MarkupLine("[green]Generating uncompressed output file[/]...");
                outputPath = Path.Combine(outputDir, jewelName);
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
                using (Stream file = File.OpenWrite(outputPath))
                {
                    file.Write(dataBuffer, 0, dataBuffer.Length);
                }
            }
            //output compressed
            if (compression == OutputCompressedFiles || compression == OutputBothFileFormats)
            {
                AnsiConsole.MarkupLine("[green]Generating compressed output file[/]...");
                byte[] compressedData = Compress(dataBuffer);
                //need to split into multiple files because PoB was dumb
                if (compressedData.Length > MaxBytesInFile)
                {
                    int splitIndex = 0;
                    byte[] split = compressedData.Take(MaxBytesInFile).ToArray();
                    while (split.Any())
                    {
                        //write the data
                        outputPath = Path.Combine(outputDir, Path.ChangeExtension(jewelName, $"zip.part{splitIndex}"));
                        if (File.Exists(outputPath))
                        {
                            File.Delete(outputPath);
                        }
                        using (Stream file = File.OpenWrite(outputPath))
                        {
                            file.Write(split, 0, split.Length);
                        }
                        split = compressedData.Skip(MaxBytesInFile * ++splitIndex).Take(MaxBytesInFile).ToArray();
                    }
                }
                //file is small enough as is, just write the file
                else
                {
                    outputPath = Path.Combine(outputDir, Path.ChangeExtension(jewelName, "zip"));
                    if (File.Exists(outputPath))
                    {
                        File.Delete(outputPath);
                    }
                    using (Stream file = File.OpenWrite(outputPath))
                    {
                        file.Write(compressedData, 0, compressedData.Length);
                    }
                }
            }
            if (isCsvExport)
            {
                AnsiConsole.MarkupLine("[green]Generating GZipped CSV file[/]...");

                outputPath = CreateCSVFile(csvExport, outputDir, jewelName, compressCSVOutput);
                
            }
            //log completion
            sw.Stop();
            AnsiConsole.MarkupLine($"{jewelName} took {string.Format("{0:0.##}", sw.Elapsed.TotalSeconds)} seconds");
            if (outputPath != null)
            {
                AnsiConsole.MarkupLine($"[blue]File available at:[/] {outputPath}");
            }
        }
        AnsiConsole.MarkupLine("[green]Done[/]!");
    }

    private static List<PassiveSkill> GetModifiableNodes(bool notables)
    {
        return DataManager.PassiveSkills.Where(x => x.IsModifiable && (!notables ^ x.IsNotable)).ToList();
    }

    private static List<PassiveSkill> GetAscendancyNotables()
    {
        return DataManager.PassiveSkills.Where(x => x.IsAscendancy && x.IsNotable).ToList();
    }

    private static void GenerateGloriousVanity(List<PassiveSkill> nodes, bool isCsvExport, Dictionary<string, int> notableJewelSocketMappings, out int[] luaDefinitions, out byte[] data, out List<CsvExportRow> csvData)
    {
        GetJewelTypeInfo(1, out int jewelMin, out int jewelMax, out int jewelIncrement, out string jewelName);
        int maxSeed = (jewelMax - jewelMin) / jewelIncrement + 1;
        //the datafile header. cant use out params in anonymous methods
        byte[] header = new byte[maxSeed * nodes.Count];
        //the actual information for the jewels. will convert to 1d later
        byte[][] data2d = new byte[maxSeed * nodes.Count][];

        //re-index our additions and replacements to consider only this jewel type
        uint numAdditions = (uint)DataManager.AlternatePassiveAdditions.Count;
        var jewelEffectOptions = DataManager.AlternatePassiveAdditions.Where(x => x.AlternateTreeVersionIndex == 1).Select(x => x.Index)
            .Concat(DataManager.AlternatePassiveSkills.Where(x => x.AlternateTreeVersionIndex == 1).Select(x => x.Index + numAdditions))
            .Select((x, i) => new { rid = x, index = (byte)i }).ToDictionary(x => x.rid, x => x.index);
        if (jewelEffectOptions.Count > 256)
        {
            throw new Exception("Cannot safely construct file: possible indices greater than byte size");
        }
        var export = new ConcurrentBag<CsvExportRow>();
        //nested parallel tasks, in case your cpu wasn't on fire yet
        Parallel.For(0, nodes.Count, nodeIndex =>
        {
            var node = nodes[nodeIndex];
            if (isCsvExport && !node.IsNotable)
            {
                // The CSV export only includes notables, so skip small nodes to save time on the alternate tree calculations
                return;
            }
            
            Parallel.For(jewelMin / jewelIncrement, jewelMax / jewelIncrement + 1, i =>
            {
                //which jewel seed is this
                i *= jewelIncrement;
                int jewelSeed = i;
                int jewelIndex = (jewelSeed - jewelMin) / jewelIncrement;
                int jewelType = 1;
                //modify the tree using that jewel
                TimelessJewel timelessJewelFromInput = GetTimelessJewel((uint)jewelSeed, (uint)jewelType);
                if (timelessJewelFromInput == null)
                    Program.ExitWithError("Failed to get the [yellow]timeless jewel[/] from input.");
                //determine how the particular node was modified
                var alternateTreeManager = new AlternateTreeManager(node, timelessJewelFromInput);
                //GV will always replace nodes
                var indices = new List<byte>();
                var rolls = new List<byte>();
                var skillInfo = alternateTreeManager.ReplacePassiveSkill();
                //handle might/legacy of the vaal
                if (skillInfo.AlternatePassiveSkill.Index == LegacyOfTheVaal || skillInfo.AlternatePassiveSkill.Index == MightOfTheVaal)
                {
                    //just shit out the stats, the fact that its legacy/might is implied
                    for (int k = 0; k < skillInfo.AlternatePassiveAdditionInformations.Count; k++)
                    {
                        //add the additions
                        indices.Add(jewelEffectOptions[skillInfo.AlternatePassiveAdditionInformations.ElementAt(k).AlternatePassiveAddition.Index]);
                        rolls.Add((byte)skillInfo.AlternatePassiveAdditionInformations.ElementAt(k).StatRolls[0]);
                    }
                }
                //handle all others
                else
                {
                    indices.Add(jewelEffectOptions[skillInfo.AlternatePassiveSkill.Index + numAdditions]);
                    for (int k = 0; k < skillInfo.StatRolls.Count; k++)
                    {
                        rolls.Add((byte)skillInfo.StatRolls[(uint)k]);
                    }
                }
                //save the data
                var dataEntry = new List<byte>(indices);
                dataEntry.AddRange(rolls);
                header[nodeIndex * maxSeed + jewelIndex] = (byte)dataEntry.Count;
                data2d[nodeIndex * maxSeed + jewelIndex] = dataEntry.ToArray();

                if(isCsvExport){
                    var exportRow = new CsvExportRow(
                        jewelSeed,
                        jewelName,
                        jewelType,
                        node,
                        (uint)nodeIndex,
                        skillInfo.AlternatePassiveSkill.Name,
                        notableJewelSocketMappings
                    );
                    // A JewelSocketId of 0 indicates that the notable does not appear
                    // in the radius of a jewel socket...so don't export it to the CSV
                    if (exportRow.JewelSocketId > 0)
                    {
                        export.Add(exportRow);
                    }
                }
            });
        });
        //write the data
        var outputData = new List<byte>(header);
        foreach (var entry in data2d.Where(d => d is not null))
        {
            outputData.AddRange(entry);
        }
        luaDefinitions = new int[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            luaDefinitions[i] = header.Skip(i * maxSeed).Take(maxSeed).Sum(x => x);
        }
        data = outputData.ToArray();
        csvData = export.ToList();
    }

    private static void GenerateAbyssal(List<PassiveSkill> nodes, bool isCsvExport, int jewelType, Dictionary<string, int> notableJewelSocketMappings, out int[] luaDefinitions, out byte[] data, out List<CsvExportRow> csvData)
    {
        GetJewelTypeInfo(jewelType, out int jewelMin, out int jewelMax, out int jewelIncrement, out string jewelName);
        int maxSeed = (jewelMax - jewelMin) / jewelIncrement + 1;
        //the datafile header. cant use out params in anonymous methods
        byte[] header = new byte[maxSeed * nodes.Count];
        //the actual information for the jewels. will convert to 1d later
        byte[][] data2d = new byte[maxSeed * nodes.Count][];

        //re-index our additions and replacements to consider only this jewel type
        uint numAdditions = (uint)DataManager.AlternatePassiveAdditions.Count;
        var jewelEffectOptions = DataManager.AlternatePassiveAdditions.Where(x => x.AlternateTreeVersionIndex == jewelType).Select(x => x.Index)
            .Concat(DataManager.AlternatePassiveSkills.Where(x => x.AlternateTreeVersionIndex == jewelType).Select(x => x.Index + numAdditions))
            .Select((x, i) => new { rid = x, index = (byte)i }).ToDictionary(x => x.rid, x => x.index);
        if (jewelEffectOptions.Count > 256)
        {
            throw new Exception("Cannot safely construct file: possible indices greater than byte size");
        }
        var export = new ConcurrentBag<CsvExportRow>();
        //nested parallel tasks, in case your cpu wasn't on fire yet
        Parallel.For(0, nodes.Count, nodeIndex =>
        {
            var node = nodes[nodeIndex];
            if (isCsvExport && !node.IsNotable)
            {
                // The CSV export only includes notables, so skip small nodes to save time on the alternate tree calculations
                return;
            }

            Parallel.For(jewelMin / jewelIncrement, jewelMax / jewelIncrement + 1, i =>
            {
                //which jewel seed is this
                i *= jewelIncrement;
                int jewelSeed = i;
                int jewelIndex = (jewelSeed - jewelMin) / jewelIncrement;
                //modify the tree using that jewel
                TimelessJewel timelessJewelFromInput = GetTimelessJewel((uint)jewelSeed, (uint)jewelType);
                if (timelessJewelFromInput == null)
                    Program.ExitWithError("Failed to get the [yellow]timeless jewel[/] from input.");
                //determine how the particular node was modified
                var alternateTreeManager = new AlternateTreeManager(node, timelessJewelFromInput);
                bool flag = node.IsAscendancy || alternateTreeManager.IsPassiveSkillReplaced();
                byte passiveSkillIndex = 0;
                uint notableAlternatePassiveIndex = 0;
                string notableReplacementName = string.Empty;
                var rolls = new List<byte>();
                if (flag)
                {
                    var skillInfo = alternateTreeManager.ReplacePassiveSkill();
                    notableAlternatePassiveIndex = skillInfo.AlternatePassiveSkill.Index;
                    passiveSkillIndex = jewelEffectOptions[notableAlternatePassiveIndex + numAdditions];
                    for (int k = 0; k < skillInfo.StatRolls.Count; k++)
                    {
                        rolls.Add((byte)skillInfo.StatRolls[(uint)k]);
                    }
                }
                else
                {
                    var skillInfo = alternateTreeManager.AugmentPassiveSkill().FirstOrDefault();
                    notableAlternatePassiveIndex = skillInfo?.AlternatePassiveAddition.Index ?? 0;
                    passiveSkillIndex = jewelEffectOptions[notableAlternatePassiveIndex];
                    for (int k = 0; k < skillInfo.StatRolls.Count; k++)
                    {
                        rolls.Add((byte)skillInfo.StatRolls[k]);
                    }
                }
                //save the data
                var dataEntry = new List<byte>
                {
                    passiveSkillIndex
                };
                dataEntry.AddRange(rolls);
                header[nodeIndex * maxSeed + jewelIndex] = (byte)dataEntry.Count;
                data2d[nodeIndex * maxSeed + jewelIndex] = dataEntry.ToArray();

                if (isCsvExport)
                {
                    //apparently this jewel snakes from the jewel socket to your starting node
                    //then modifies one of your ascendancy nodes
                    //so the logic of "is this in radius of a jewel socket" does not apply
                }
            });
        });
        //write the data
        var outputData = new List<byte>(header);
        foreach (var entry in data2d.Where(d => d is not null))
        {
            outputData.AddRange(entry);
        }
        luaDefinitions = new int[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            luaDefinitions[i] = header.Skip(i * maxSeed).Take(maxSeed).Sum(x => x);
        }
        data = outputData.ToArray();
        csvData = export.ToList();
    }

    private static void GenerateRegular(List<PassiveSkill> nodes, bool isCsvExport, int jewelType, Dictionary<string, int> notableJewelSocketMappings, out byte[] data, out List<CsvExportRow> csvData)
    {
        GetJewelTypeInfo(jewelType, out int jewelMin, out int jewelMax, out int jewelIncrement, out string jewelName);

        int maxSeed = (jewelMax - jewelMin) / jewelIncrement + 1;
        byte[] dataInternal = new byte[maxSeed * nodes.Count];
        //re-index our additions and replacements to consider only this jewel type
        uint numAdditions = (uint)DataManager.AlternatePassiveAdditions.Count;
        var jewelEffectOptions = DataManager.AlternatePassiveAdditions.Where(x => x.AlternateTreeVersionIndex == jewelType).Select(x => x.Index)
            .Concat(DataManager.AlternatePassiveSkills.Where(x => x.AlternateTreeVersionIndex == jewelType).Select(x => x.Index + numAdditions))
            .Select((x, i) => new { rid = x, index = (byte)i }).ToDictionary(x => x.rid, x => x.index);

        Dictionary<uint, string> notableJewelReplacementNames;
        var timelessJewelsWithAdditions = new string[] { "LethalPride", "BrutalRestraint" };
        if (timelessJewelsWithAdditions.Contains(jewelName))
        {
            notableJewelReplacementNames = DataManager.AlternatePassiveAdditions
                .Where(x => x.AlternateTreeVersionIndex == jewelType)
                .ToDictionary(x => x.Index, x => x.Name);
        }
        else
        {
            notableJewelReplacementNames = DataManager.AlternatePassiveSkills
                .Where(x => x.AlternateTreeVersionIndex == jewelType)
                .ToDictionary(x => x.Index, x => x.Name);
        }

        if (jewelEffectOptions.Count > 256)
        {
            throw new Exception("Cannot safely construct file: possible indices greater than byte size");
        }

        var export = new ConcurrentBag<CsvExportRow>();
        //for non glorious vanity, we only care about notables
        Parallel.For(0, nodes.Count, notableIndex =>
        {
            var notable = nodes[notableIndex];
            Parallel.For(jewelMin / jewelIncrement, jewelMax / jewelIncrement + 1, i =>
            {
                //which jewel seed is this
                i *= jewelIncrement;
                int jewel_seed = i;
                int jewel_index = (jewel_seed - jewelMin) / jewelIncrement;
                int jewel_type = jewelType;
                //modify the tree using that jewel
                TimelessJewel timelessJewelFromInput = GetTimelessJewel((uint)jewel_seed, (uint)jewel_type);
                if (timelessJewelFromInput == null)
                    Program.ExitWithError("Failed to get the [yellow]timeless jewel[/] from input.");
                //figure out how it affects this specific notable
                var alternateTreeManager = new AlternateTreeManager(notable, timelessJewelFromInput);
                bool flag = alternateTreeManager.IsPassiveSkillReplaced();
                byte passiveSkillIndex = 0;
                uint notableAlternatePassiveIndex = 0;
                string notableReplacementName = string.Empty;
                if (flag)
                {
                    notableAlternatePassiveIndex = alternateTreeManager.ReplacePassiveSkill().AlternatePassiveSkill.Index;
                    notableReplacementName = notableJewelReplacementNames[notableAlternatePassiveIndex];
                    passiveSkillIndex = jewelEffectOptions[notableAlternatePassiveIndex + numAdditions];
                }
                else
                {
                    notableAlternatePassiveIndex = alternateTreeManager.AugmentPassiveSkill().FirstOrDefault()?.AlternatePassiveAddition.Index ?? 0;
                    notableReplacementName = notableJewelReplacementNames[notableAlternatePassiveIndex];
                    passiveSkillIndex = jewelEffectOptions[notableAlternatePassiveIndex];
                }

                if(isCsvExport){
                    var exportRow = new CsvExportRow(
                        jewel_seed,
                        jewelName,
                        jewel_type,
                        notable,
                        notableAlternatePassiveIndex,
                        notableReplacementName,
                        notableJewelSocketMappings
                    );
                    // A JewelSocketId of 0 indicates that the notable does not appear in the radius of a jewel socket
                    if (exportRow.JewelSocketId > 0)
                    {
                        export.Add(exportRow);
                    }
                }

                dataInternal[notableIndex * maxSeed + jewel_index] = passiveSkillIndex;
            });
        });
        data = dataInternal;
        csvData = export.ToList();
    }

    private static void GetJewelTypeInfo(int jewelType, out int jewelMin, out int jewelMax, out int jewelIncrement, out string jewelName)
    {
        switch (jewelType)
        {
            case 1:
                jewelMin = 100;
                jewelMax = 8000;
                jewelIncrement = 1;
                jewelName = "GloriousVanity";
                break;
            case 2:
                jewelMin = 10000;
                jewelMax = 18000;
                jewelIncrement = 1;
                jewelName = "LethalPride";
                break;
            case 3:
                jewelMin = 500;
                jewelMax = 8000;
                jewelIncrement = 1;
                jewelName = "BrutalRestraint";
                break;
            case 4:
                jewelMin = 2000;
                jewelMax = 10000;
                jewelIncrement = 1;
                jewelName = "MilitantFaith";
                break;
            case 5:
                jewelMin = 2000;
                jewelMax = 160000;
                jewelIncrement = 20;
                jewelName = "ElegantHubris";
                break;
            case 6:
                jewelMin = 100;
                jewelMax = 8000;
                jewelIncrement = 1;
                jewelName = "HeroicTragedy";
                break;
            case 7:
                jewelMin = 100;
                jewelMax = 8000;
                jewelIncrement = 1;
                jewelName = "FesteringVengeance";
                break;
            case 8:
                jewelMin = 100;
                jewelMax = 8000;
                jewelIncrement = 1;
                jewelName = "ExtinguishingGrasp";
                break;
            case 9:
                jewelMin = 100;
                jewelMax = 8000;
                jewelIncrement = 1;
                jewelName = "BalefulDominion";
                break;
            case 10:
                jewelMin = 100;
                jewelMax = 8000;
                jewelIncrement = 1;
                jewelName = "DestructiveAspirations";
                break;
            case 11:
                jewelMin = 100;
                jewelMax = 8000;
                jewelIncrement = 1;
                jewelName = "ReclaimedMalevolence";
                break;
            default:
                ExitWithError($"Unrecognized jewel type code: [yellow]{jewelType}[/].");
                jewelMin = 0;
                jewelMax = 0;
                jewelIncrement = 1;
                jewelName = "UnknownJewel";
                break;
        }
    }

    private static byte[] Compress(byte[] data)
    {
        using (var internalMemoryStream = new MemoryStream())
        {
            //deflate it to the smallest size. we have time.
            using (var deflateStream = new ZLibStream(internalMemoryStream, CompressionLevel.SmallestSize))
            {
                deflateStream.Write(data, 0, data.Length);
            }
            return internalMemoryStream.ToArray();
        }        
    }

    private static TimelessJewel GetTimelessJewel(uint seed, uint jewelType)
    {
        AlternateTreeVersion alternateTreeVersion = DataManager.AlternateTreeVersions
            .First(q => (q.Index == jewelType));
        return new TimelessJewel(alternateTreeVersion, seed);
    }

    private static void WaitForExit()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Press [yellow]any key[/] to exit.");

        try
        {
            Console.ReadKey();
        }
        catch { }

        Environment.Exit(0);
    }

    private static void PrintError(string error)
    {
        AnsiConsole.MarkupLine($"[red]Error[/]: {error}");
    }

    private static void ExitWithError(string error)
    {
        PrintError(error);
        WaitForExit();
    }

    private static void PromptUserForFile(string query, out string response, bool isDir = false)
    {
        TextPrompt<string> fileTextPrompt = new TextPrompt<string>(query)
            .Validate((string input) =>
            {
                if (!isDir && !File.Exists(input))
                {
                    return ValidationResult.Error($"[red]Error[/]: Unable to find file: '{input}'");
                }
                return ValidationResult.Success();
            });
        response = AnsiConsole.Prompt(fileTextPrompt);
    }

    private static void PromptUserForChoice(string query, List<string> choices, out int response)
    {
        SelectionPrompt<string> fileTextPrompt = new SelectionPrompt<string>().Title(query);
        foreach (string choice in choices)
        {
            fileTextPrompt.AddChoice(choice);
        }
        response = choices.IndexOf(AnsiConsole.Prompt(fileTextPrompt));
    }

    private static string CreateCSVFile(List<CsvExportRow> csvExport, string outputDir, string outputFile, bool compress = false)
    {
        var outputPath = Path.Combine(outputDir, Path.ChangeExtension(outputFile, compress ? "csv.gz" : "csv"));
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        if(compress) {

            using var fileStream = File.Create(outputPath);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Compress);
            using var writer = new StreamWriter(gzipStream);
            writer.Write(SerializeCSV(csvExport));

        } else
        {
            File.WriteAllText(outputPath, SerializeCSV(csvExport));
        }

        return outputPath;
    }

    private static string SerializeCSV(List<CsvExportRow> csvExport)
    {
        return CsvSerializer.SerializeToCsv(
            csvExport
                .OrderBy(s => s.JewelSeed)
                .ThenBy(s => s.JewelSocketId)
        );
    }
}
