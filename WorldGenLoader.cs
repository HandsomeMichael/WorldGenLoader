using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using Terraria.ModLoader.Config.UI;
using Terraria.UI;
using Terraria.UI.Chat;
using System.IO;
using Terraria.DataStructures;
using Terraria.GameInput;
using Terraria.ModLoader.IO;
using Terraria.Localization;
using Terraria.Utilities;
using System.Reflection;
using MonoMod.RuntimeDetour.HookGen;
using Microsoft.Xna.Framework.Audio;
using Terraria.Audio;
using Terraria.Graphics.Capture;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using ReLogic.Graphics;
using System.Runtime;
using Microsoft.Xna.Framework.Input;
using Terraria.Graphics.Shaders;
using Terraria.GameContent.Generation;
using Terraria.World.Generation;
using System.Diagnostics;
using System.Threading;

namespace WorldGenLoader
{
	public class WorldGenLoader : Mod
	{
		public static bool HasAtleastOneGenPass;
		public static List<GenPass> genpasses;
		public static GenerationProgress progress;
		public static bool forcedfast;
		public static int loadedgenpass;
		public override void PreSaveAndQuit() {
			genpasses = null;
			progress = null;
			forcedfast = false;
			loadedgenpass = 0;
		}
		public override void Load() {
			HasAtleastOneGenPass = false;
			forcedfast = false;
			genpasses = null;
			progress = null;
			loadedgenpass = 0;
		}
		public override void PostAddRecipes() {
			LoadGen(true);
		}
		public override void PostDrawInterface(SpriteBatch spriteBatch) {
			if (progress != null) {
				string titleText = progress.Message;
				if (titleText == "" && genpasses != null) {
					titleText = genpasses[loadedgenpass].Name;
				}
				var snippets = ChatManager.ParseMessage(titleText, Color.White).ToArray();
				Vector2 messageSize = ChatManager.GetStringSize(Main.fontDeathText, snippets, Vector2.One);
				Vector2 pos = new Vector2(Main.screenWidth/2,Main.screenHeight/2).Floor();
				ChatManager.DrawColorCodedStringWithShadow(spriteBatch, Main.fontDeathText, snippets, pos, 0f, messageSize/2f, Vector2.One/2f, out int hover);
				pos.Y += messageSize.Y/2;
				titleText = genpasses == null ? $"( {progress.TotalProgress*100f}% )" : $"( {loadedgenpass+1}/{genpasses.Count} )";
				snippets = ChatManager.ParseMessage(titleText, Color.White).ToArray();
				messageSize = ChatManager.GetStringSize(Main.fontMouseText, snippets, Vector2.One);
				ChatManager.DrawColorCodedStringWithShadow(spriteBatch, Main.fontMouseText, snippets, pos, 0f, messageSize/2f, Vector2.One, out hover);
			}
		}
		public static void LoadGen(bool save) {
			genpasses = GetDummyGenPass();
			float totalWeight = 1f;
			WorldHooks.ModifyWorldGenTasks(genpasses, ref totalWeight);
			foreach (var item in genpasses){
				if (!GetVanillaGenPass().Contains(item.Name)) {
					if (!HasAnyName(item.Name)) {
						HasAtleastOneGenPass = true;
						MyConfig.get.GenPassList.Add(item.Name,MyConfig.get.GenDefaultValue);
						if (save) {MyConfig.Save();}
					}
				}
			}
			genpasses = null;
		}
		public static bool HasAnyName(string name) {
			if (MyConfig.get == null) {return true;}
			if (MyConfig.get.GenPassList.Count == 0) {return false;}
			foreach (var item in MyConfig.get.GenPassList){if (item.Key == name) {return true;}}
			return false;
		}
		public override void Unload() {
			progress = null;
			genpasses = null;
		}
		public override void PostUpdateEverything() {
			if (MyConfig.get.GenPassList.Count == 0 && HasAtleastOneGenPass) {
				LoadGen(true);
				Main.NewText("Worldgen list cleared",Color.Pink);
				progress = null;
				genpasses = null;
				loadedgenpass = 0;
				return;
			}
			if (!MyConfig.get.GenSmooth || forcedfast) {
				if (genpasses != null && progress != null) {
					if (loadedgenpass == genpasses.Count) {
						Main.NewText($"Done ! loaded {genpasses.Count} world gen");
						progress = null;
						genpasses = null;
						loadedgenpass = 0;
						forcedfast = false;
						return;
					}
					int speed = MyConfig.get.GenSpeed;
					if (forcedfast && speed < 50) {speed = 50;}
					for (int i = 0; i < speed; i++){
						if (loadedgenpass == genpasses.Count) {return;}
						genpasses[loadedgenpass].Apply(progress);
						Main.NewText($"WorldGen '{progress.Message}' {loadedgenpass+1}/{genpasses.Count}");
						loadedgenpass++;
					}
				}
			}
		}
		public static void HardWorldGenLoad(object context) {
			try{
				foreach (var item in genpasses){
					genpasses[loadedgenpass].Apply(progress);
					if (genpasses == null) {return;}
					if (loadedgenpass == genpasses.Count) {break;}
					Main.NewText($"WorldGen '{progress.Message}' {loadedgenpass+1}/{genpasses.Count}");
					loadedgenpass++;
				}
				Main.NewText($"Done ! loaded {genpasses.Count} world gen");
				progress = null;
				genpasses = null;
				loadedgenpass = 0;
			}
			catch (Exception e){ModContent.GetInstance<WorldGenLoader>().Logger.Error((object)Language.GetTextValue("tModLoader.WorldGenError"), e);}
		}
		public class cancelworldgen : ModCommand
		{
			public override CommandType Type=> CommandType.Chat;
			public override string Command=> "cancelworldgen";
			public override string Usage => "/cancelworldgen <skip || noskip>";
			public override string Description
				=> "Cancel currently runned world gen code";
			public override void Action(CommandCaller caller, string input, string[] args) {
				if (args.Length > 0 && args[0] == "skip") {
					loadedgenpass = genpasses.Count;
					caller.Reply("succesfully skipped");
					return;
				}
				genpasses = null;
				progress = null;
				loadedgenpass = 0;
				caller.Reply("succesfully canceled");
			}
		}
		public class preworldgen : ModCommand
		{
			public override CommandType Type=> CommandType.Chat;
			public override string Command=> "preworldgen";
			public override string Usage => "/preworldgen <modinternalname>";
			public override string Description
				=> "Load modded pre world gen, if mod internal name doesnt get inputted then it will load every mod PreWorldGen";

			public override void Action(CommandCaller caller, string input, string[] args) {
				if (args.Length == 0) {
					for (int a = 0; a < MyConfig.get.GenTimes; a++){WorldHooks.PreWorldGen();}
					caller.Reply($"Succesfully loaded every PreWorldGen");
					return;
				}
				bool success = false;
				foreach (ModWorld world in GetWorlds()){
					string name = world.mod.Name;
					name = name.ToUpper();
					string check = args[0].ToUpper();
					if (name == check) {
						for (int a = 0; a < MyConfig.get.GenTimes; a++){world.PreWorldGen();}
						success = true;
					}
				}
				if (success) {caller.Reply($"Succesfully load '{args[0]}' PreWorldGen");}
				else {caller.Reply($"Failed to find any '{args[0]}' mod world");}
			}
		}
		public class postworldgen : ModCommand
		{
			public override CommandType Type=> CommandType.Chat;
			public override string Command=> "postworldgen";
			public override string Usage => "/postworldgen <modinternalname>";
			public override string Description
				=> "Load modded Post World Gen, if mod internal name doesnt get inputted then it will load every mod PreWorldGen";

			public override void Action(CommandCaller caller, string input, string[] args) {
				if (args.Length == 0) {
					for (int a = 0; a < MyConfig.get.GenTimes; a++){WorldHooks.PostWorldGen();}
					caller.Reply($"Succesfully loaded every PostWorldGen");
					return;
				}
				bool success = false;
				foreach (ModWorld world in GetWorlds()){
					string name = world.mod.Name;
					name = name.ToUpper();
					string check = args[0].ToUpper();
					if (name == check) {
						for (int a = 0; a < MyConfig.get.GenTimes; a++){world.PostWorldGen();}
						success = true;
					}
				}
				if (success) {caller.Reply($"Succesfully load '{args[0]}' PostWorldGen");}
				else {caller.Reply($"Failed to find any '{args[0]}' mod world");}
			}
		}
		public class hardmodeworldgen : ModCommand
		{
			public override CommandType Type=> CommandType.Chat;
			public override string Command=> "hardmodeworldgen";
			public override string Usage => "/hardmodeworldgen <times>";
			public override string Description
				=> "load hardmode world gen";

			public override void Action(CommandCaller caller, string input, string[] args) {
				int times = 1;
				if (args.Length > 0) {times = int.Parse(args[0]);}
				caller.Reply($"Loading {times} hardmode worldgen");
				if (Main.netMode != 1){
					ThreadPool.QueueUserWorkItem(GenHardmode, times);
				}
			}
			public static void GenHardmode(object context) {
				int times = Convert.ToInt32(context);
				for (int i = 0; i < times; i++){
					if (Main.netMode != 1){
						Main.NewText($"Loading hardmode worldgen {i}/{times}",Color.Pink);
						WorldGen.smCallBack(1);
					}
				}
			}
		}
		public class smashaltargen : ModCommand
		{
			public static int X;
			public static int Y;
			public override CommandType Type=> CommandType.Chat;
			public override string Command=> "smashaltargen";
			public override string Usage => "/smashaltargen <times>";
			public override string Description
				=> "load smash altar gen";

			public override void Action(CommandCaller caller, string input, string[] args) {
				int times = 1;
				if (args.Length > 0) {times = int.Parse(args[0]);}
				caller.Reply($"Loading {times} altar smash world gen");
				X = (int)caller.Player.position.X/16;
				Y = (int)caller.Player.position.Y/16;
				genpasses = new List<GenPass>();
				progress = new GenerationProgress();
				loadedgenpass = 0;
				WorldGen.altarCount = 2 + times;
				for (int a = 0; a < times; a++){
					genpasses.Add(new PassLegacy("Altarsmash"+a,delegate(GenerationProgress prog) {
						prog.Message = "Altar smashing..";
						Main.hardMode = true;
						WorldGen.SmashAltar(X,Y);
					}));
				}
				WorldGen.noTileActions = false;
				WorldGen.gen = false;
				forcedfast = true;
			}
		}
		public class loadworldgen : ModCommand
		{
			public override CommandType Type=> CommandType.Chat;
			public override string Command=> "loadworldgen";
			public override string Usage => "/loadworldgen < modinternalname || anymod > <times>";
			public override string Description
				=> "Load modded Genpass, if mod internal name not inputted or set to 'anymod' it will load any mod genpass";

			public List<T> CloneList <T>(List<T> clone) {
				var list = new List<T>();
				foreach (var item in clone){list.Add(item);}
				return list;
			}
			public override void Action(CommandCaller caller, string input, string[] args) {
				genpasses = GetDummyGenPass();//CloneList<GenPass>(vanillagenpasses);
				float totalWeight = 1f;
				if (args.Length == 0 || (args.Length > 0 && args[0] == "anymod")) {
					WorldHooks.ModifyWorldGenTasks(genpasses, ref totalWeight);
					int time = 0;
					if (args.Length > 1) {time = int.Parse(args[1]);}
					Generate(time);
					return;
				}
				else if (args.Length > 0) {
					bool success = false;
					foreach (ModWorld world in GetWorlds()){
						string name = world.mod.Name;
						name = name.ToUpper();
						string check = args[0].ToUpper();
						if (name == check) {
							world.ModifyWorldGenTasks(genpasses, ref totalWeight);
							success = true;
						}
					}
					int time = 0;
					if (args.Length > 1) {time = int.Parse(args[1]);}
					if (success) {Generate(time);}
					else {
						caller.Reply($"Failed to find any '{args[0]}' mod world");
						genpasses = null;
					}
				}
			}
		}
		public class generateworld : ModCommand
		{
			public override CommandType Type=> CommandType.Chat;
			public override string Command=> "generateworld";
			public override string Usage => "/generateworld <seed>";
			public override string Description
				=> "Generate world by overriding the current one, leave the seed empty for the current seed. may broke ur game lol";
			public override void Action(CommandCaller caller, string input, string[] args) {
				int seed = Main.worldID;
				if (args.Length > 0) {seed = args[0].GetHashCode();}
				progress = new GenerationProgress();
				ThreadPool.QueueUserWorkItem(TryGenerateWorld, new Tuple<GenerationProgress,int>(progress,Main.worldID));
			}
			public static void TryGenerateWorld(object threadContext){
				try{
					var context = threadContext as Tuple<GenerationProgress,int>;
					Main.PlaySound(10);
					WorldGen.generateWorld(context.Item2 , context.Item1);
				}
				catch (Exception e){ModContent.GetInstance<WorldGenLoader>().Logger.Error((object)Language.GetTextValue("tModLoader.WorldGenError"), e);}
			}
		}
		public class npcloot : ModCommand
		{
			public override CommandType Type=> CommandType.Chat;
			public override string Command=> "npcloot";
			public override string Usage => "/npcloot <id>";
			public override string Description
				=> "call npcloot, used for mod that tried to do worldgen when a certain npc is killed";
			public override void Action(CommandCaller caller, string input, string[] args) {
				if (args.Length < 1) {
					caller.Reply("need atleast 1 argument");
					return;
				}
				int id = int.Parse(args[0]);
				NPC npc = new NPC();
				npc.SetDefaults(id);
				npc.Center = caller.Player.Center;
				npc.NPCLoot();
				caller.Reply("successfully called npcloot");
			}
		}
		public static void Generate(int time = 0) {
			if (MyConfig.get.GenPassList.Count == 0 && HasAtleastOneGenPass) {
				LoadGen(true);
			}
			WorldGen.SetupStatueList();
			bool hasWorldGen = false;
			var dellist = new List<GenPass>();
			foreach (var item in genpasses){
				if (GetVanillaGenPass().Contains(item.Name)) {dellist.Add(item);}
				else if (!MyConfig.get.GenPassList[item.Name]) {
					dellist.Add(item);
					hasWorldGen = true;
				}
			}
			foreach (var del in dellist){genpasses.Remove(del);}
			if (genpasses.Count == 0) {
				if (hasWorldGen) {Main.NewText("No Worldgen enabled, try enabling some in mod config");}
				else {Main.NewText("No Worldgen detected, try using some mod that add world gen");}
				genpasses = null;
				return;
			}
			int count = genpasses.Count;
			if (time == 0) {time = MyConfig.get.GenTimes-1;}
			if (time > 0) {
				for (int i = 0; i < time; i++){
					for (int a = 0; a < count; a++) {genpasses.Add(genpasses[a]);}
				}
				count = genpasses.Count;
			}
			Main.NewText($"Loading {count} world gen");
			progress = new GenerationProgress();
			loadedgenpass = 0;
			if (MyConfig.get.GenSmooth && !forcedfast) {
				ThreadPool.QueueUserWorkItem(HardWorldGenLoad, null);
			}
		}
		public static IList<ModWorld> GetWorlds() {
			var field = typeof(WorldHooks).GetField("worlds", BindingFlags.NonPublic | BindingFlags.Static);
			return (IList<ModWorld>)field.GetValue(null);
		}
		[Label("Worldgen Loader")]
		public class MyConfig : ModConfig{
			public static void Save(){
				typeof(ConfigManager).GetMethod("Save", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[1] { get });
			}
			public override ConfigScope Mode => ConfigScope.ClientSide;
			public static MyConfig get => ModContent.GetInstance<MyConfig>();

			[Label("Worldgen Smooth Generation")]
			[Tooltip("Smoothly generate worldgen")]
			[DefaultValue(true)]
			public bool GenSmooth;

			[Label("Worldgen unsmooth speed")]
			[Tooltip("The speed of not smooth worldgen, may freezes the game if you set it to high number\ndefault to 1")]
			[Range(1, 100)]
			[Slider]
			[DefaultValue(1)]
			public int GenSpeed;

			[Label("Worldgen default generation times")]
			[Tooltip("How many time it will generate. default to 1")]
			[Range(1, 1000)]
			[DefaultValue(1)]
			public int GenTimes;

			[Label("Worldgen list enable / disable all")]
			[Tooltip("enable / disable all worldgen list\nclear the worldgen list ingame to take effect")]
			[DefaultValue(false)]
			public bool GenDefaultValue;

			[Label("Modded Worldgen list")]
			[Tooltip("List of modded gen pass, you can enable / disable using the 'Value'\nClear ingame to reset list")]
			public Dictionary<string,bool> GenPassList = new Dictionary<string,bool>();
			public override void OnChanged() {
				if (MyConfig.get != null) {
					if (GenPassList.Count == 0) {
						LoadGen(true);
					}
				}
			}
		}
		public static List<GenPass> GetDummyGenPass() {
			var passes = new List<GenPass>();
			foreach (var item in GetVanillaGenPass()){passes.Add(new PassLegacy(item,delegate{}));}
			return passes;
		}
		public static string[] GetVanillaGenPass() => new string[] {
			"Reset",
			"Terrain",
			"Tunnels",
			"Sand",
			"Mount Caves",
			"Dirt Wall Backgrounds",
			"Rocks In Dirt",
			"Dirt In Rocks",
			"Clay",
			"Small Holes",
			"Dirt Layer Caves",
			"Rock Layer Caves",
			"Surface Caves",
			"Slush Check",
			"Grass",
			"Jungle",
			"Marble",
			"Granite",
			"Mud Caves To Grass",
			"Full Desert",
			"Floating Islands",
			"Mushroom Patches",
			"Mud To Dirt",
			"Silt",
			"Shinies",
			"Webs",
			"Underworld",
			"Lakes",
			"Dungeon",
			"Corruption",
			"Slush",
			"Mud Caves To Grass",
			"Beaches",
			"Gems",
			"Gravitating Sand",
			"Clean Up Dirt",
			"Pyramids",
			"Dirt Rock Wall Runner",
			"Living Trees",
			"Wood Tree Walls",
			"Altars",
			"Wet Jungle",
			"Remove Water From Sand",
			"Jungle Temple",
			"Hives",
			"Jungle Chests",
			"Smooth World",
			"Settle Liquids",
			"Waterfalls",
			"Ice",
			"Wall Variety",
			"Traps",
			"Life Crystals",
			"Statues",
			"Buried Chests",
			"Surface Chests",
			"Jungle Chests Placement",
			"Water Chests",
			"Spider Caves",
			"Gem Caves",
			"Moss",
			"Temple",
			"Ice Walls",
			"Jungle Trees",
			"Floating Island Houses",
			"Quick Cleanup",
			"Pots",
			"Hellforge",
			"Spreading Grass",
			"Piles",
			"Moss",
			"Spawn Point",
			"Grass Wall",
			"Guide",
			"Sunflowers",
			"Planting Trees",
			"Herbs",
			"Dye Plants",
			"Webs And Honey",
			"Weeds",
			"Mud Caves To Grass",
			"Jungle Plants",
			"Vines",
			"Flowers",
			"Mushrooms",
			"Stalac",
			"Gems In Ice Biome",
			"Random Gems",
			"Moss Grass",
			"Muds Walls In Jungle",
			"Larva",
			"Settle Liquids Again",
			"Tile Cleanup",
			"Lihzahrd Altars",
			"Micro Biomes",
			"Final Cleanup"
		};
		public class ItemCaller : CommandCaller {

			public CommandType CommandType => CommandType.Chat;
			public Player Player => _player;
			internal Player _player;

			public void Reply(string text, Color color = default(Color)) {
				Main.NewText(text);
			}

			public ItemCaller(Player player) {this._player = player;}
		}
		public abstract class WLStaff : ModItem {
			public virtual string name => Name;
			public virtual string tooltip => "";
			public virtual ModCommand Command => null;
			public virtual string[] args => new string[0];
			public override bool UseItem(Player player) {
				var p = Command;
				p.Action(new ItemCaller(player),player.name,args);
				return true;
			}
			public override void SetStaticDefaults() {
				DisplayName.SetDefault(name);
				Tooltip.SetDefault(tooltip);
			}
			public override void SetDefaults() {
				item.useTime = item.useAnimation = 20;
				item.width = item.height = 14;
				item.useStyle = 1;
				item.rare = -12;
				item.UseSound = SoundID.Item1;
			}
		}
		public class preworldgenstaff : WLStaff {
			public override string name => "Pre World Gen Staff";
			public override string tooltip => "Generate modded pre worldgen";
			public override ModCommand Command => new preworldgen();
		}
		public class postworldgenstaff : WLStaff {
			public override string name => "Post World Gen Staff";
			public override string tooltip => "Generate modded post worldgen";
			public override ModCommand Command => new postworldgen();
		}
		public class generateworldstaff : WLStaff {
			public override string name => "World Generator";
			public override string tooltip => "Generate a whole world, may break the game";
			public override ModCommand Command => new generateworld();
		}
		public class hardmodeworldgenstaff : WLStaff {
			public override string name => "Hardmode World Gen Staff";
			public override string tooltip => "Generate Hardmode Worldgen";
			public override ModCommand Command => new hardmodeworldgen();
		}
		public class smashaltargenstaff : WLStaff {
			public override string name => "Demon altar worldgen staff";
			public override string tooltip => "Generate ores from demon altar";
			public override ModCommand Command => new smashaltargen();
		}
		public class loadworldgenstaff : WLStaff {
			public override string name => "Worldgen Loader Staff";
			public override string tooltip => "Generate world gen based on the mod config";
			public override ModCommand Command => new loadworldgen();
			public override void ModifyTooltips(List<TooltipLine> tooltips) {
				tooltips.Add(new TooltipLine(mod, "wow thats a lot", $"List of enabled world gen :"));
				int i = 0;
				foreach (var item in MyConfig.get.GenPassList){
					if (item.Value) {
						tooltips.Add(new TooltipLine(mod, "List"+i, item.Key));
						i++;
					}
				}
				
				if (i == 0) {
					string text = "No worldgen enabled, try enabling some";
					if (!HasAtleastOneGenPass) {
						text = "No mod with worldgen detected, try using mod with world gen";
					}
					var tt = new TooltipLine(mod, "notooltips??",text);
					tt.overrideColor = Color.Pink;
					tooltips.Add(tt);
				}
				tooltips.Add(new TooltipLine(mod, "Count",$"( {i} out of {MyConfig.get.GenPassList.Count} enabled )"));
				
			}
		}
	}
}