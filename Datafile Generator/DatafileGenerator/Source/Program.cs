using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectre.Console;
using DatafileGenerator.Data;
using DatafileGenerator.Data.Models;
using DatafileGenerator.Game;
using System.IO.Compression;

namespace DatafileGenerator;

public static class Program
{
    private const int MightOfTheVaal = 76;
    private const int LegacyOfTheVaal = 77;
    private const int MaxBytesInFile = 5242880; //5MB
    private const int ExpectedArgumentCount = 5;
    private static int NumAdditions;
    private static bool IsInteractiveMode = true;
    private const string LuaMappingFileName = "NodeIndexMapping.lua";
    private const string CsvFileName = "node_indices.csv";

    public static void Main(string[] args)
    {
        Console.Title = $"{GeneratorSettings.ApplicationName} (v{GeneratorSettings.ApplicationVersion})";
        AnsiConsole.MarkupLine("Spinning up!");
        if (IsHelpRequested(args))
        {
            PrintUsage();
            return;
        }
        string outputDir;
        int compression;
        if (args != null && args.Length > 0)
        {
            IsInteractiveMode = false;
            if (!TryParseCommandLineArguments(args, out outputDir, out compression, out string argumentError))
            {
                PrintUsage();
                ExitWithError(argumentError);
                return;
            }
            AnsiConsole.MarkupLine("[grey]Using command-line arguments.[/]");
        }
        else
        {
            IsInteractiveMode = true;

            PromptUserForFile("Path to [yellow]alternate passive ADDITIONS[/] file:", out GeneratorSettings.AlternatePassiveAdditionsFilePath);
            PromptUserForFile("Path to [yellow]alternate passive SKILLS[/] file:", out GeneratorSettings.AlternatePassiveSkillsFilePath);
            PromptUserForFile("Path to [yellow]skill tree[/] file:", out GeneratorSettings.PassiveSkillsFilePath);
            PromptUserForFile("Path to [yellow]output[/] directory:", out outputDir, true);
            PromptUserForChoice("Output type:", new List<string>() { "compressed", "uncompressed", "both" }, out compression);
            compression += 1; //hacky but it turns it into a bitmask by doing this
        }
        AnsiConsole.MarkupLine("[green]Loading[/]...");

        if (!DataManager.Initialize())
            ExitWithError("Failed to initialize the [yellow]data manager[/].");
        NumAdditions = DataManager.AlternatePassiveAdditions.Count;
        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

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
        //begin iterating over the 6 jewel types
        //reverse order since glorious vanity sucks
        for (int i = 6; i > 0; i--)
        {
            var sw = Stopwatch.StartNew();
            GetJewelTypeInfo(i, out _, out _, out _, out string outputFile);
            byte[] dataBuffer;
            //glorious vanity logic
            if (i == 1)
            {
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
                GenerateRegular(justNotables, i, out dataBuffer);
            }
            //output uncompressed
            if ((compression & 2) == 2)
            {
                string outputPath = Path.Combine(outputDir, outputFile);
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
            if ((compression & 1) == 1)
            {
                byte[] compressedData = Compress(dataBuffer);
                //need to split into multiple files because PoB is dumb
                if (compressedData.Length > MaxBytesInFile)
                {
                    int splitIndex = 0;
                    byte[] split = compressedData.Take(MaxBytesInFile).ToArray();
                    while (split.Any())
                    {
                        //write the data
                        string outputPath = Path.Combine(outputDir, Path.ChangeExtension(outputFile, $"zip.part{splitIndex}"));
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
                    string outputPath = Path.Combine(outputDir, Path.ChangeExtension(outputFile, "zip"));
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
            //log completion
            sw.Stop();
            Console.WriteLine($"{outputFile} took {sw.Elapsed.TotalSeconds} seconds");
        }
        AnsiConsole.MarkupLine("[green]Done[/]!");
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
                    //do we want to add the indicator for this being Legacy of the Vaal/Might of the Vaal or just shit out the stats?
                    //indices.Add((byte)(skillInfo.AlternatePassiveSkill.Index + NumAdditions));
                    for (int k = 0; k < skillInfo.AlternatePassiveAdditionInformations.Count; k++)
                    {
                        //add the additions
                        indices.Add((byte)skillInfo.AlternatePassiveAdditionInformations.ElementAt(k).AlternatePassiveAddition.Index);
                        rolls.Add((byte)skillInfo.AlternatePassiveAdditionInformations.ElementAt(k).StatRolls[0U]);
                    }
                }
                //handle all others
                else
                {
                    indices.Add((byte)(skillInfo.AlternatePassiveSkill.Index + NumAdditions));
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

    private static void GenerateRegular(List<PassiveSkill> nodes, int jewelType, out byte[] data)
    {
        GetJewelTypeInfo(jewelType, out int jewelMin, out int jewelMax, out int jewelIncrement, out _);
        int maxSeed = (jewelMax - jewelMin) / jewelIncrement + 1;
        byte[] dataInternal = new byte[maxSeed * nodes.Count];
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
                    //replacements get stat rid + count(alternate_passive_additions)
                    passiveSkillIndex = (byte)(alternateTreeManager.ReplacePassiveSkill().AlternatePassiveSkill.Index + NumAdditions);
                }
                else
                {
                    //additions get stat rid as is
                    passiveSkillIndex = (byte)alternateTreeManager.AugmentPassiveSkill().First().AlternatePassiveAddition.Index;
                }
                dataInternal[notableIndex * maxSeed + jewel_index] = passiveSkillIndex;
            });
        });
        data = dataInternal;
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
            .First(q => (q.Index == jewelType));
        return new TimelessJewel(alternateTreeVersion, seed);
    }

    private static bool TryParseCommandLineArguments(string[] args, out string outputDir, out int compression, out string error)
    {
        outputDir = null;
        compression = 0;
        error = null;

        if (args.Length != ExpectedArgumentCount)
        {
            error = $"Expected {ExpectedArgumentCount} arguments, received {args.Length}.";
            return false;
        }

        string additionsFilePath = args[0];
        string skillsFilePath = args[1];
        string treeFilePath = args[2];
        outputDir = args[3];
        string compressionMode = args[4];

        if (!File.Exists(additionsFilePath))
        {
            error = $"Unable to find alternate passive additions file: '{additionsFilePath}'.";
            return false;
        }

        if (!File.Exists(skillsFilePath))
        {
            error = $"Unable to find alternate passive skills file: '{skillsFilePath}'.";
            return false;
        }

        if (!File.Exists(treeFilePath))
        {
            error = $"Unable to find skill tree file: '{treeFilePath}'.";
            return false;
        }

        if (File.Exists(outputDir))
        {
            error = $"Output path points to a file, expected a directory: '{outputDir}'.";
            return false;
        }

        if (!TryParseCompressionMode(compressionMode, out compression))
        {
            error = $"Unrecognized output type '{compressionMode}'. Use compressed, uncompressed, both, or 1/2/3.";
            return false;
        }

        GeneratorSettings.AlternatePassiveAdditionsFilePath = additionsFilePath;
        GeneratorSettings.AlternatePassiveSkillsFilePath = skillsFilePath;
        GeneratorSettings.PassiveSkillsFilePath = treeFilePath;

        return true;
    }

    private static bool TryParseCompressionMode(string compressionMode, out int compression)
    {
        compression = 0;

        if (string.IsNullOrWhiteSpace(compressionMode))
            return false;

        if (int.TryParse(compressionMode, out int numericCompression) &&
            numericCompression >= 1 && numericCompression <= 3)
        {
            compression = numericCompression;
            return true;
        }

        switch (compressionMode.Trim().ToLowerInvariant())
        {
            case "compressed":
                compression = 1;
                return true;
            case "uncompressed":
                compression = 2;
                return true;
            case "both":
                compression = 3;
                return true;
            default:
                return false;
        }
    }

    private static bool IsHelpRequested(string[] args)
    {
        return args != null &&
            args.Length == 1 &&
            (args[0] == "-h" || args[0] == "--help" || args[0] == "/?");
    }

    private static void PrintUsage()
    {
        AnsiConsole.WriteLine("Usage:");
        AnsiConsole.WriteLine("  DataFileGenerator <alternate_additions_json> <alternate_skills_json> <skill_tree_json> <output_dir> <output_type>");
        AnsiConsole.WriteLine("  output_type: compressed | uncompressed | both | 1 | 2 | 3");
    }

    private static void WaitForExit(int exitCode = 0)
    {
        if (IsInteractiveMode)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("Press [yellow]any key[/] to exit.");

            try
            {
                Console.ReadKey();
            }
            catch { }
        }

        Environment.Exit(exitCode);
    }

    private static void PrintError(string error)
    {
        AnsiConsole.MarkupLine($"[red]Error[/]: {error}");
    }

    private static void ExitWithError(string error)
    {
        PrintError(error);
        WaitForExit(1);
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
}
