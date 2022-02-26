﻿using System;
using Verse;
using RimWorld;
using System.Collections.Generic;
using Verse.AI;
using System.Reflection;
using UnityEngine;

namespace SR
{
	public class JobDriver_Dig : JobDriver_AffectFloor
	{
		protected Dictionary<TerrainDef, ThingDef> _noCostItemGuessCache = new Dictionary<TerrainDef, ThingDef>();
		protected float workLeft = -1000f;
		protected float workTotal = 0;

		protected override int BaseWorkAmount
		{
			get
			{
				return 800; //smoothing a floor was 2800
			}
		}

		protected override DesignationDef DesDef
		{
			get
			{
				return DesignationDefOf.SR_Dig;
			}
		}

		protected override StatDef SpeedStat
		{
			get
			{
				return StatDefOf.MiningSpeed;
			}
		}

		protected virtual SkillDef Skill
        {
			get
            {
				return SkillDefOf.Mining;
            }
        }

		public JobDriver_Dig()
		{
			clearSnow = true;
		}

		protected override void DoEffect(IntVec3 c)
		{
			TerrainDef ot = c.GetTerrain(Map);
			if (!ot.affordances.Contains(TerrainAffordanceDefOf.Diggable)) //If the terrain isn't diggable (maybe the terrain was swapped out dynamically, e.g., LakesCanFreeze)..
				return; //Abort.
			TerrainDef ut;
			//Generic mod compatibility support (go by costList, else guess by defName/label (and cache guess for performance), else warn user in log)
			if (ot.costList == null || ot.costList.Count == 0) //No costList..
			{
				bool newKey = true;
				ThingDef toDrop = null;
				int toDropAmount = Rand.Range(6, 10);
				if (_noCostItemGuessCache.ContainsKey(ot)) //Check cache.
				{
					toDrop = _noCostItemGuessCache[ot]; //Get from cache.
					newKey = false;
				}
				else //Wasn't cached, we'll make a new guess..
				{
					var defNameLowerInvariant = ot.defName.ToLowerInvariant();
					var labelLowerInvariant = ot.label.ToLowerInvariant();
					if (defNameLowerInvariant.Contains("soil") || defNameLowerInvariant.Contains("dirt") || labelLowerInvariant.Contains("soil") || labelLowerInvariant.Contains("dirt"))
					{
						if (defNameLowerInvariant.Contains("fertile") || defNameLowerInvariant.Contains("rich") || labelLowerInvariant.Contains("fertile") || labelLowerInvariant.Contains("rich"))
							toDrop = SoilDefs.SR_RichSoil;
						else if (defNameLowerInvariant.Contains("stony") || defNameLowerInvariant.Contains("rocky") || labelLowerInvariant.Contains("stony") || labelLowerInvariant.Contains("rocky"))
							toDrop = SoilDefs.SR_Gravel;
						else
							toDrop = SoilDefs.SR_Soil;
					}
					else if (defNameLowerInvariant.Contains("ice") || labelLowerInvariant.Contains("ice"))
						toDrop = SoilDefs.SR_Ice;
					else if (defNameLowerInvariant.Contains("sand") || labelLowerInvariant.Contains("sand"))
						toDrop = SoilDefs.SR_Sand;
					else if (defNameLowerInvariant.Contains("gravel") || labelLowerInvariant.Contains("gravel"))
						toDrop = SoilDefs.SR_Gravel;
					else
						SoilRelocation.Log("Unsupported soil \"" + ot.defName + "\" AKA \"" + ot.label + "\" being dug, was not able to guess what to drop, report this to the creator of the mod it came from or UdderlyEvelyn to fix this.", ErrorLevel.Warning);
					if (newKey)
						_noCostItemGuessCache.Add(ot, toDrop); //Cache it for later.
				}
				if (toDrop != null) //If we have drops..
				{
					#region WaterFreezesHandling
					//Handle WaterFreezes Ice..
					if (ot.defName == "WF_LakeIceThin" || 
						ot.defName == "WF_LakeIce" || 
						ot.defName == "WF_LakeIceThick" || 
						ot.defName == "WF_MarshIceThin" || 
						ot.defName == "WF_MarshIce" ||
						ot.defName == "WF_RiverIceThin" ||
						ot.defName == "WF_RiverIce" ||
						ot.defName == "WF_RiverIceThick")
					{
						toDropAmount = Math.Max(1, Mathf.RoundToInt(WaterFreezes_Interop.TakeCellIce(Map, c).Value / 25 * toDropAmount));
						var water = WaterFreezes_Interop.QueryCellAllWater(Map, c);
						var waterDepth = WaterFreezes_Interop.QueryCellWater(Map, c);
						//The below has two different cases for mud, might be refactorable?
						//SoilRelocation.Log("WF Compat.. utIsWater: " + utIsWater + ", naturalWater: " + naturalWater?.defName + ", isNaturalWater: " + isNaturalWater + ", water: " + water + ", toDropAmount: " + toDropAmount);
						if (waterDepth <= 0) //If natural water isn't null or under-terrain is water but there's no water at that tile..
						{
							if (Map.Biome.defName == "SeaIce") //Special case for sea ice biomes, can't have it giving mud, makes no sense!
								Map.terrainGrid.SetTerrain(c, TerrainDefOf.WaterOceanDeep);
							else
								Map.terrainGrid.SetTerrain(c, TerrainDefs.Mud); //Set the terrain to mud to represent the sediment under the water normally.
						}
						else if (waterDepth > 0) //If it's natural water and there's more than 0 water..
							Map.terrainGrid.SetTerrain(c, water); //Set it to its water type.
						else //This should never happen, but log if it does.
							SoilRelocation.Log("Attempted to dig WaterFreezes ice but did not fit a recognized circumstance.", ErrorLevel.Error);
						Utilities.DropThing(Map, c, toDrop, toDropAmount); //Drop the item
						return; //Don't need to run the rest of the code, WF has special handling above.
					}
					#endregion //Handle WaterFreezes special support..
					Utilities.DropThing(Map, c, toDrop, toDropAmount); //Drop the item
				}
			}
			else //costList present, use that.
				Utilities.DropThings(Map, c, ot.costList, 2, 1);
			ut = Map.terrainGrid.UnderTerrainAt(c); //Get under-terrain
			if (ut != null) //If there was under-terrain..
				Map.terrainGrid.SetTerrain(c, ut); //Set the top layer to the under-terrain
			else //No under-terrain
			{
				if (Map.Biome.defName == "SeaIce" && ot == TerrainDefOf.Ice) //Special case for sea ice biomes, can't have it giving stone to work with and allowing deep drilling!
					Map.terrainGrid.SetTerrain(c, TerrainDefOf.WaterOceanDeep);
				else //All other cases..
					Map.terrainGrid.SetTerrain(c, Map.GetComponent<CMS.MapComponent_StoneGrid>().StoneTerrainAt(c)); //Set the terrain to the natural stone for this area to represent bedrock
			}
			FilthMaker.RemoveAllFilth(c, Map);
		}

		protected override IEnumerable<Toil> MakeNewToils()
		{
			this.FailOn(() => (!job.ignoreDesignations && base.Map.designationManager.DesignationAt(base.TargetLocA, DesDef) == null) ? true : false);
			yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.Touch);
			Toil doWork = new Toil();
			doWork.initAction = delegate
			{
				var target = base.TargetLocA;
				var currentTerrain = Map.terrainGrid.TerrainAt(target);
				if (!currentTerrain.affordances.Contains(TerrainAffordanceDefOf.Diggable))
				{
					Map.designationManager.DesignationAt(base.TargetLocA, DesDef)?.Delete(); //Get rid of the designation, invalid.
					ReadyForNextToil(); //Don't keep trying to do this job.
				}
				if (currentTerrain.defName == "LCF_LakeIceThin" || currentTerrain.defName == "LCF_LakeIce" || currentTerrain.defName == "LCF_LakeIceThick")
					workTotal = (BaseWorkAmount / 2) + (WaterFreezes_Interop.QueryCellIce(Map, base.TargetLocA).Value / 100) * (BaseWorkAmount / 2);
				else
					workTotal = BaseWorkAmount;
				workLeft = workTotal;
			};
			doWork.tickAction = delegate
			{
				float num = ((SpeedStat != null) ? doWork.actor.GetStatValue(SpeedStat) : 1f);
				num *= 1.7f;
				workLeft -= num;
				if (doWork.actor.skills != null)
				{
					doWork.actor.skills.Learn(Skill, 0.1f);
				}
				if (clearSnow)
				{
					base.Map.snowGrid.SetDepth(base.TargetLocA, 0f);
				}
				if (workLeft <= 0f)
				{
					DoEffect(base.TargetLocA);
					base.Map.designationManager.DesignationAt(base.TargetLocA, DesDef)?.Delete();
					ReadyForNextToil();
				}
			};
			doWork.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
			doWork.WithProgressBar(TargetIndex.A, () => 1f - workLeft / (float)workTotal);
			doWork.defaultCompleteMode = ToilCompleteMode.Never;
			doWork.activeSkill = () => Skill;
			yield return doWork;
		}
	}
}