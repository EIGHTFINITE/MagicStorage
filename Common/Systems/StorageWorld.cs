using MagicStorage.Components;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.ItemDropRules;
using Terraria.IO;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.WorldBuilding;

namespace MagicStorage.Common.Systems
{
	public class StorageWorld : ModSystem
	{
		public override void PreSaveAndQuit() {
			StoragePlayer.LocalPlayer.CloseStorage();
		}

		public override void PreWorldGen() => OnWorldLoad();

		public override void ClearWorld() => OnWorldLoad();

		private const int MIGRATION_VERSION_0_6_1 = 1;

		public override void SaveWorldData(TagCompound tag)
		{
			tag["migration"] = MIGRATION_VERSION_0_6_1;

			if (!Main.dedServ)
				MagicStorageMod.Instance.optionsConfig.Save();
		}

		public override void LoadWorldData(TagCompound tag)
		{
			int migration = tag.GetInt("migration");
			if (migration < MIGRATION_VERSION_0_6_1) {
				Mod.Logger.Debug("Migrating world to v0.6.1 state...");

				// Scan the entire world for Storage Access components, and place the tile entity if it doesn't exist
				// Also force all storage systems to recalculate their components
				TEStorageAccess accessTE = ModContent.GetInstance<TEStorageAccess>();

				List<Point16> accessesToPlace = new();
				List<TEStorageHeart> heartsToUpdate = new();
				List<TERemoteAccess> remotesToUpdate = new();

				for (int x = 0; x < Main.maxTilesX; x++) {
					for (int y = 0; y < Main.maxTilesY; y++) {
						Tile tile = Main.tile[x, y];

						if (tile.HasTile && tile.TileFrameX == 0 && tile.TileFrameY == 0) {
							Point16 pos = new(x, y);
							if (tile.TileType == ModContent.TileType<StorageAccess>()) {
								if (!TileEntity.ByPosition.ContainsKey(pos))
									accessesToPlace.Add(pos);
							} else if (tile.TileType == ModContent.TileType<StorageHeart>()) {
								if (TileEntity.ByPosition.TryGetValue(pos, out TileEntity te) && te is TEStorageHeart heart)
									heartsToUpdate.Add(heart);
							} else if (tile.TileType == ModContent.TileType<RemoteAccess>()) {
								if (TileEntity.ByPosition.TryGetValue(pos, out TileEntity te) && te is TERemoteAccess remote)
									remotesToUpdate.Add(remote);
							}
						}
					}
				}

				// NOTE: ModSystem.LoadWorldData runs AFTER tile enties load their data!
				//       This is preferable since some components have new data in the migration, and that will be set here
				//       when the network's connections are recalculated.
				foreach (Point16 pos in accessesToPlace)
					accessTE.Place(pos.X, pos.Y);

				foreach (TEStorageHeart heart in heartsToUpdate)
					heart.ResetAndSearch();

				foreach (TERemoteAccess remote in remotesToUpdate)
					remote.ResetAndSearch();

				int numAccesses = accessesToPlace.Count;
				int numNetworks = heartsToUpdate.Count + remotesToUpdate.Count;
				Mod.Logger.Debug($"Success!  Placed {numAccesses} new Storage Access entit{(numAccesses == 1 ? "y" : "ies")}, recalculated components for {numNetworks} storage network{(numNetworks == 1 ? "" : "s")}");
			}
		}

		public override void PostUpdateEverything() {
			// Tile Entity logic does not run on clients
			// Hence, the delayed sound playing on storage hearts has to be handled here
			foreach (TEStorageCenter center in TileEntity.ByPosition.Values.OfType<TEStorageCenter>())
				center.UpdateItemEatingTime();
		}
	}
}
