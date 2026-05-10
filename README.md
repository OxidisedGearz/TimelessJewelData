# Timeless Jewel Datafile Generator

## Before you begin

This tool requires several data files as inputs. Follow these steps to source the information:

1. Clone this git repo from <https://github.com/Regisle/TimelessJewelData/tree/Generator>
2. Open the latest branch
3. Set up the `data.json` file by:
   1. Downloading the `data.json` tree file from GGG's github: <https://github.com/grindinggear/skilltree-export/blob/master/data.json>
   2. Saving the `data.json` file as `~\Datafile Generator\DatafileGenerator\source-data\data.json`
4. Open <https://snosme.github.io/poe-dat-viewer/>
5. The page shows you the **Latest PoE patch**. Paste the PoE1 Patch number into the **Patch #** field and click the **Import** button
6. Set up the `alternatepassiveadditions.json` file by:
   1. Use the search bar to find `AlternatePassiveAdditions`, open it and click on **Export data** in the top right
   2. Save the `alternatepassiveadditions.json` file as `~\Datafile Generator\DatafileGenerator\source-data\alternatepassiveadditions.json`
7. Set up the `alternatepassiveskills.json` file by:
   1.  Use the search bar to find `AlternatePassiveSkills`, open it and click on **Export data** in the top right
   2.  Save the `alternatepassiveskills.json` file as `~\Datafile Generator\DatafileGenerator\source-data\alternatepassiveskills.json`

## Using the data files

### Parsing Brutal Restraint, Elegant Hubris, Lethal Pride, Militant Faith, and Heroic Tragedy

Data files are uint8 arrays (1 byte per node+seed) in a pure binary format, where array\[node_id_INDEX \* jewel_seed_Size + jewel_seed_offset\] = index_of_change

	node_id_INDEX is given by Node_Indices.csv
	jewel_seed_Size is the number of seeds for a given jewel (note elegant hubris seeds are divided by 20)
	jewel_seed_offset is the value above the minimum  (note elegant hubris seeds are divided by 20)

A list of which nodes are in range of what jewel socket can be found in Jewel_Node_Link.json

Also note that Node_Indices.csv contains indices for all modifiable nodes, but the non-Glorious Vanity jewels will only have data for notables. Modifications to non-notables for these jewels are constant and thus have been omitted from their data files to save space.

to find index_of_change:

  parse alternate_passive_additions.json selecting only the entries that match the jewel type, ordered by _rid
  parse alternate_passive_skills.json selecting only the entries that match the jewel type, ordered by _rid
  concatenate the two, placing alternate_passive_additions before those of alternate_passive_skills
  entry with index index_of_change will be the stat that was selected

Non-Glorious Vanity jewels are relatively simple to parse with this definition:

The suggested method for parsing non-GV is to:
- convert/load the entire file into a unit8 array, 
- create a list of valid notables you want (by above index) (only do 1 jewel socket at a time)
- create an array of weights, (most will be 0)
- create an array of valid seeds
- SEEK to a location in the uint8 array, and input the value as the index into your weight_array to obtain the weight of the node, add this value to the value in your seed_array
- once you have gone through all nodes/seeds, go through the list and remove any that fall below some chosen threshold
- then sort the seed_array from largest to smallest

It is also possible to SEEK directly to the byte in the file that holds the desired information, but for most use cases, doing so offers little to no benefit.

### Parsing Glorious Vanity

Glorious Vanity, having variable stat replacements for * *all* * nodes as well as multiple stats per notable with rolls on those stats, is a much larger file (hence compressed for github), and is more complex. Its parsing method is similar to the others, but requires a fair few tweaks:

like before:

	node_id_INDEX is given by Node_Indices.csv
	jewel_seed_Size is the number of seeds for a given jewel
	jewel_seed_offset is the value above the minimum

to find index_of_change:

  parse alternate_passive_additions.json selecting only the entries that match the jewel type, ordered by _rid
  parse alternate_passive_skills.json selecting only the entries that match the jewel type, ordered by _rid
  concatenate the two, placing alternate_passive_additions before those of alternate_passive_skills
  entry with index index_of_change will be the stat that was selected

and data files are in a pure binary format with one byte per piece of information; however, with glorious vanity, each node requires multiple pieces of information.


For a jewel, each node can have multiple changes, and each change comes with 1 or 2 associated stat values (roll value for roll range), as such there is a header section at the start of the file with the size of the amount of data each jewel holds for a given node.
- eg if it has 4 stats it has an 8 in the header (4 stats with 1 roll each)
- if it has 1 stat with 2 rolls it has a 3 in the header

To know the length of the header, we have the additional definition:

	nodeCount is the number of nodes in the Node_Indices.csv

Because each node has more than 1 value associated with it, the recommended method for parsing it is a header array and a 2d array of data as follows:
- create a header of size: nodeCount \* maxSeedIndex
- create a variable length 2d array for Data. the first coordinate, similar to the other jewels, will be the index of the node index \* maxSeedIndex + seed index, but since each stat needs multiple bytes, the value at that coordinate will be an array of bytes instead of just a single byte
- load in the data an array of size equal to the value in header\[i\] into data\[i\] which gives you the full 2d array
- when you iterate over it like in the non-GV method you can then access specific elements to check if its the change you want, or use the values for weighted sums
- eg for 1 stat (header\[i\]==2) its, data\[i\]\[0\] to check the 0th change, and data\[i\]\[1\] to get the value of the 0th change, where "i" is the same index formula as the non-GV version, but can access the non-notable indices in Node_Indices.csv
- note that its all the stats then all the rolls, not stat, roll, stat, roll, eg for 3 stats its \[0\]stat1, \[1\]stat2, \[2\]stat3, \[3\]roll1, \[4\]roll2, \[5\]roll3
- theres only 4 cases: 1 stat 1 roll, 1 stat 2 rolls, 3 stats 3 rolls, 4 stats 4 rolls

Additionally, note that glorious vanity always replaces its nodes. Only Might of the Vaal and Legacy of the Vaal will ever have "additions" on them, and even those additions are really just replacements for the original notable's stats. Process them accordingly.

### Examples

a basic example in C# of parsing both kinds of jewels as part of a weighted search has been provided by OxidisedGearz and can be found in examples folder

#### examples of how to load data files into byte arrays by @zao:

Python
```python
lut = pathlib.Path('Militant Faith').read_bytes()
```

C++
```c++
std::ifstream is("Militant Faith", std::ios::binary);
auto file_size = is.seekg(0, std::ios::end); // or use std::filesystem::file_size on a path
is.seekg(0, std::ios::beg);
std::vector<uint8_t> lut(file_size);
is.read((char*)lut.data(), lut.size());
```

#### example of grabbing a single node:

take a random node, lets say lethal pride, Lava Lash, seed 10116 (as it ends up easier), this gives you an index of 0 + 116, the byte at that value is 52 (a "4" in ascii) which corresponds with "karui_notable_add_burning_damage", which is what it is ![](https://cdn.discordapp.com/attachments/175290321695932416/993077938847219722/unknown.png)

## Generating the data files

Datafiles are generated using the DatafileGenerator (a visual studio project, C#).    
It's built on top of a timeless jewel simulator, so its not very concise, but the meat of the file format logic is in program.cs while the rest is just modelling the prng and parsing jsons.

It will need an alternate passive additions json, an alternate passive skills json, and the most recent skill tree json. You'll also have to tell it where to output and whether you want the compressed or uncompressed files.    

Running it will output 6 datafiles, 1 lua file, and 1 csv file (note that if compressed, the Glorious Vanity "file" will actually come out to be multiple files each with size at most 5MB due to the limitations within Path of Building).

## Generating CSV files

If you'd like to export human-readable CSV data dumps of the Timeless Jewel notable replacements, do the following

1. Follow the steps outlines in [Before you begin](#before-you-begin)
2. Open `~\Datafile Generator\DatafileGenerator.sln` in Visual Studio
3. (Optional) Add or remove `[IgnoreDataMember]` annotations in the `~\Datafile Generator\DatafileGenerator\Source\Data\Models\CsvExportRow.cs` file to specify which data fields you'd like to include in your CSV exports
4. Run the program. If you are prompted for input files then you have saved the 3 json files in the wrong directory. You can still select them manually
5. Choose **csv** as the export type
6. The GZipped CSV files are exported to `~\Datafile Generator\output-data`
