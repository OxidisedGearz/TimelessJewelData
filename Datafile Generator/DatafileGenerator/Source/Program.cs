using DatafileGenerator.Data;
using DatafileGenerator.Data.Models;
using DatafileGenerator.Game;
using DatafileGenerator.Source.Data.Models;
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
    private const string LuaMappingFileName = "NodeIndexMapping.lua";
    private const string CsvFileName = "node_indices.csv";

    public static void Main()
    {
        Console.Title = $"{GeneratorSettings.ApplicationName} (v{GeneratorSettings.ApplicationVersion})";
        //prompt
        AnsiConsole.MarkupLine("Spinning up!");

        // Load input files
        GeneratorSettings.AlternatePassiveAdditionsFilePath = Path.GetFullPath(@"source-data\alternatepassiveadditions.json");
        if(!File.Exists(GeneratorSettings.AlternatePassiveAdditionsFilePath)){
            PromptUserForFile("Path to [yellow]alternate passive ADDITIONS[/] file:", out GeneratorSettings.AlternatePassiveAdditionsFilePath);
        }
        GeneratorSettings.AlternatePassiveSkillsFilePath = Path.GetFullPath(@"source-data\alternatepassiveskills.json");
        if(!File.Exists(GeneratorSettings.AlternatePassiveAdditionsFilePath)){
            PromptUserForFile("Path to [yellow]alternate passive SKILLS[/] file:", out GeneratorSettings.AlternatePassiveSkillsFilePath);
        }
        GeneratorSettings.PassiveSkillsFilePath = Path.GetFullPath(@"source-data\data.json");
        if(!File.Exists(GeneratorSettings.AlternatePassiveAdditionsFilePath)){
            PromptUserForFile("Path to [yellow]skill tree[/] file:", out GeneratorSettings.PassiveSkillsFilePath);
        }
        var outputDir = Path.GetFullPath(@"output-data");
        if(!File.Exists(GeneratorSettings.AlternatePassiveAdditionsFilePath)){
            PromptUserForFile("Path to [yellow]output[/] directory:", out outputDir, true);
        }
        
        var exportOptions = new List<string>() { "compressed", "uncompressed", "csv", "both" };
        PromptUserForChoice("Output type:", exportOptions, out int compressionChoice);
        var compression = exportOptions.ElementAt(compressionChoice);

        AnsiConsole.MarkupLine("[green]Loading[/]...");

        if (!DataManager.Initialize())
            ExitWithError("Failed to initialize the [yellow]data manager[/].");
        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        Dictionary<string, int> notableJewelSocketmappings = null;
        if (compression == "csv")
        {
            AnsiConsole.MarkupLine("[green]Calculating Notable Mappings[/]...");
            var calculator = new AffectedNotablesCalculator();
            notableJewelSocketmappings = calculator.GetNotableToSocketMapping();
            AnsiConsole.MarkupLine($"{notableJewelSocketmappings.Count} Notable Mappings Loaded");
        }

        var justNotables = GetModifiableNodes(true);
        var justSmallNodes = GetModifiableNodes(false);
        justNotables.Sort();
        justSmallNodes.Sort();
        //generate our indices
        var notablesThenSmalls = justNotables.ToList();
        notablesThenSmalls.AddRange(justSmallNodes);
        AnsiConsole.MarkupLine("[green]Processing[/]...");
        //build the csv
        if (File.Exists(CsvFileName))
            File.Delete(CsvFileName);
        var sb = new StringBuilder("PassiveSkillGraphId,Name,Datafile Parsing Index\n");
        for (int k = 0; k < notablesThenSmalls.Count; k++)
        {
            var node = notablesThenSmalls[k];
            sb.AppendLine(node.GraphIdentifier + "," + (node.Name.Contains(',') ? ("\"" + node.Name + "\"") : node.Name) + "," + k);
        }
        File.WriteAllText(Path.Combine(outputDir, CsvFileName), sb.ToString());
        sb.Clear();
        //begin iterating over the 5 jewel types
        //reverse order since glorious vanity sucks
        string outputPath = null;
        for (int i = 6; i > 0; i--)
        {
            var sw = Stopwatch.StartNew();
            GetJewelTypeInfo(i, out _, out _, out _, out string outputFile);
            byte[] dataBuffer;
            List<CsvExportRow> csvExport;
            //glorious vanity logic
            if (i == 1)
            {
                AnsiConsole.MarkupLine("[green]Calculating Glorious Vanity Seeds[/]...");
                // TODO - Remove this temporary skip logic for glorious vanity
                continue;

                //calculate
                GenerateGloriousVanity(notablesThenSmalls, out var luaSizes, out dataBuffer);
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
                File.WriteAllText(Path.Combine(outputDir, LuaMappingFileName), sb.ToString());
                sb.Clear();
            }
            //non-glorious vanity logic
            else
            {
                GenerateRegular(justNotables, i, notableJewelSocketmappings, out dataBuffer, out csvExport);
            }
            //output uncompressed
            if (compression == "uncompressed" || compression == "both")
            {
                AnsiConsole.MarkupLine("[green]Generating Uncompressed Output File[/]...");
                outputPath = Path.Combine(outputDir, outputFile);
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
                using Stream file = File.OpenWrite(outputPath);
                file.Write(dataBuffer, 0, dataBuffer.Length);
            }
            //output compressed
            if (compression == "compressed" || compression == "both")
            {
                AnsiConsole.MarkupLine("[green]Generating Compressed Output File[/]...");
                byte[] compressedData = Compress(dataBuffer);
                //need to split into multiple files because PoB is dumb
                if (compressedData.Length > MaxBytesInFile)
                {
                    int splitIndex = 0;
                    byte[] split = compressedData.Take(MaxBytesInFile).ToArray();
                    while (split.Any())
                    {
                        //write the data
                        outputPath = Path.Combine(outputDir, Path.ChangeExtension(outputFile, $"zip.part{splitIndex}"));
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
                    outputPath = Path.Combine(outputDir, Path.ChangeExtension(outputFile, "zip"));
                    if (File.Exists(outputPath))
                    {
                        File.Delete(outputPath);
                    }
                    using Stream file = File.OpenWrite(outputPath);
                    file.Write(compressedData, 0, compressedData.Length);
                }
            }
            if (compression == "csv")
            {
                AnsiConsole.MarkupLine("[green]Generating CSV File[/]...");
                outputPath = Path.Combine(outputDir, Path.ChangeExtension(outputFile, "csv"));
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }

                File.WriteAllText(outputPath, CsvSerializer.SerializeToCsv(csvExport));
            }
            //log completion
            sw.Stop();
            AnsiConsole.MarkupLine($"{outputFile} took {string.Format("{0:0.##}", sw.Elapsed.TotalSeconds)} seconds");
            if (outputPath != null)
            {
                AnsiConsole.MarkupLine($"[blue]File available at:[/] {outputPath}");
            }
        }
        AnsiConsole.MarkupLine("[blue]Done[/]!");
    }

    private static List<PassiveSkill> GetModifiableNodes(bool notables)
    {
        return DataManager.PassiveSkills.Where(x => x.IsModifiable && (!notables ^ x.IsNotable)).ToList();
    }

    private static void GenerateGloriousVanity(List<PassiveSkill> nodes, out int[] luaDefinitions, out byte[] data)
    {
        GetJewelTypeInfo(1, out int jewelMin, out int jewelMax, out int jewelIncrement, out _);
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
        //nested parallell tasks, in case your cpu wasnt on fire yet
        Parallel.For(0, nodes.Count, nodeIndex =>
        {
            var node = nodes[nodeIndex];
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
                        rolls.Add((byte)skillInfo.AlternatePassiveAdditionInformations.ElementAt(k).StatRolls[0U]);
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
            });
        });
        //write the data
        var outputData = new List<byte>(header);
        foreach (var entry in data2d)
        {
            outputData.AddRange(entry);
        }
        luaDefinitions = new int[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            luaDefinitions[i] = header.Skip(i * maxSeed).Take(maxSeed).Sum(x => x);
        }
        data = outputData.ToArray();
    }

    private static void GenerateRegular(List<PassiveSkill> nodes, int jewelType, Dictionary<string, int> notableJewelSocketmappings, out byte[] data, out List<CsvExportRow> csvData)
    {
        GetJewelTypeInfo(jewelType, out int jewelMin, out int jewelMax, out int jewelIncrement, out string jewelName);

        AnsiConsole.MarkupLine($"[green]Calculating {jewelName} Seeds[/]...");

        int maxSeed = (jewelMax - jewelMin) / jewelIncrement + 1;
        byte[] dataInternal = new byte[maxSeed * nodes.Count];

        //re-index our additions and replacements to consider only this jewel type
        uint numAdditions = (uint)DataManager.AlternatePassiveAdditions.Count;

        var jewelEffectOptions =
            DataManager.AlternatePassiveAdditions
                .Where(x => x.AlternateTreeVersionIndex == jewelType)
                .Select(x => x.Index)
                .Concat(
                    DataManager.AlternatePassiveSkills
                        .Where(x => x.AlternateTreeVersionIndex == jewelType)
                        .Select(x => x.Index + numAdditions)
                )
                .Select((x, i) => new { rid = x, index = (byte)i })
                .ToDictionary(x => x.rid, x => x.index);

        var notableJewelReplacements = DataManager.AlternatePassiveSkills
                .Where(x => x.AlternateTreeVersionIndex == jewelType)
                .ToDictionary(x => x.Index, x => x);

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
                if (flag)
                {
                    var notableIndex = alternateTreeManager.ReplacePassiveSkill().AlternatePassiveSkill.Index;
                    passiveSkillIndex = jewelEffectOptions[notableIndex + numAdditions];
                    var exportRow = new CsvExportRow(
                        jewel_seed,
                        jewelName,
                        jewel_type,
                        notable,
                        notableIndex,
                        notableJewelReplacements[notableIndex],
                        notableJewelSocketmappings
                    );
                    // A JewelSocketId of 0 indicates that the notable does not appear in the radius of a jewel socket
                    if (exportRow.JewelSocketId > 0)
                    {
                        export.Add(exportRow);
                    }
                }
                else
                {
                    passiveSkillIndex = jewelEffectOptions[alternateTreeManager.AugmentPassiveSkill().First().AlternatePassiveAddition.Index];
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
        var internalMemoryStream = new MemoryStream();
        //deflate it to the smallest size. we have time.
        using (var deflateStream = new ZLibStream(internalMemoryStream, CompressionLevel.SmallestSize))
        {
            deflateStream.Write(data, 0, data.Length);
        }
        return internalMemoryStream.ToArray();
    }

    private static TimelessJewel GetTimelessJewel(uint seed, uint jewelType)
    {
        AlternateTreeVersion alternateTreeVersion = DataManager.AlternateTreeVersions
            .First(q => q.Index == jewelType);
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
            .Validate(input =>
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
}
