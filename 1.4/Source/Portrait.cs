using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using Verse;

namespace PortraitsOfTheRim
{
    [HotSwappable]
    [StaticConstructorOnStartup]
    public class Portrait : IExposable
    {
        private static readonly Texture2D ChangeStyles = ContentFinder<Texture2D>.Get("UI/ChangeStyles");
        private static readonly Texture2D ShowHideHatOff = ContentFinder<Texture2D>.Get("UI/ShowHideHat");
        private static readonly Texture2D ShowHidePortraitOff = ContentFinder<Texture2D>.Get("UI/ShowHidePortrait");
        private static readonly Texture2D ShowHideHatOn = ContentFinder<Texture2D>.Get("UI/ShowHideHaton");
        private static readonly Texture2D ShowHidePortraitOn = ContentFinder<Texture2D>.Get("UI/ShowHidePortraiton");
        private static readonly Texture2D OutlineTex = SolidColorMaterials.NewSolidColorTexture(new ColorInt(77, 77, 77).ToColor);
        public Pawn pawn;
        private List<(PortraitElementDef, Texture)> portraitTextures;
        private int lastCreatingTime;

        public bool hidePortrait = !PortraitsOfTheRimSettings.showPortraitByDefault;
        public bool hideHeadgear;
        public string currentStyle;

        private List<(PortraitElementDef, Texture)> PortraitTextures
        {
            get
            {
                if (portraitTextures is null || Time.frameCount % 20 == 0 && lastCreatingTime != Time.frameCount)
                {
                    lastCreatingTime = Time.frameCount;
                    portraitTextures = GetPortraitTextures();
                    var missingLayers = PortraitUtils.layers.Where(x => portraitTextures.Any(y => y.Item1.portraitLayer == x) is false);
                    var existingLayers = PortraitUtils.layers.Where(x => portraitTextures.Any(y => y.Item1.portraitLayer == x));
                    //Log.Message("MissingLayers: " + string.Join(", ", missingLayers));
                    //Log.Message("Existing layers: " + string.Join(", ", existingLayers));
                    //Log.Message("Drawn textures: " + string.Join(", ", portraitTextures.Select(x => x.Item2.name)));
                }
                return portraitTextures;
            }
        }

        public bool ShouldShow => hidePortrait is false;
        public void RenderPortrait(float x, float y, float width, float height)
        {
            var textures = PortraitTextures;
            var renderRect = new Rect(x, y, width, height);
            Widgets.DrawBoxSolid(renderRect, Widgets.WindowBGFillColor);
            foreach (var texture in textures)
            {
                if (this.hideHeadgear && PortraitUtils.HeadgearLayers.Contains(texture.Item1.portraitLayer))
                    continue;
                GUI.DrawTexture(renderRect, texture.Item2);
            }
            Widgets.DrawBox(renderRect.ExpandedBy(1), 1, OutlineTex);
        }

        public void DrawButtons(float x, float y)
        {
            Rect hidePortraitRect = HidePortraitButton(x, y);
            var hideHeadgear = new Rect(hidePortraitRect.x, hidePortraitRect.yMax + 5, 24, 24);
            TooltipHandler.TipRegion(hideHeadgear, this.hideHeadgear ? "PR.ShowHeadgear".Translate() : "PR.HideHeadgear".Translate());
            Widgets.DrawBoxSolidWithOutline(hideHeadgear, Color.black, Color.grey);
            if (Widgets.ButtonImage(hideHeadgear, this.hideHeadgear ? ShowHideHatOff : ShowHideHatOn))
            {
                this.hideHeadgear = !this.hideHeadgear;
            }

            var selectStyle = new Rect(hidePortraitRect.x, hideHeadgear.yMax + 5, 24, 24);
            TooltipHandler.TipRegion(selectStyle, "PR.SelectStyle".Translate());
            Widgets.DrawBoxSolidWithOutline(selectStyle, Color.black, Color.grey);
            if (Widgets.ButtonImage(selectStyle, ChangeStyles))
            {
                var floatList = new List<FloatMenuOption>();
                foreach (var style in PortraitUtils.allStyles)
                {
                    floatList.Add(new FloatMenuOption(style.CapitalizeFirst(), delegate
                    {
                        this.currentStyle = style;
                    }));
                }
                floatList.Add(new FloatMenuOption("None".Translate(), delegate
                {
                    this.currentStyle = null;
                }));
                Find.WindowStack.Add(new FloatMenu(floatList));
            }
        }

        public Rect HidePortraitButton(float x, float y)
        {
            var hidePortraitRect = new Rect(x, y, 24, 24);
            TooltipHandler.TipRegion(hidePortraitRect, this.hidePortrait ? "PR.ShowPortrait".Translate() : "PR.HidePortrait".Translate());
            Widgets.DrawBoxSolidWithOutline(hidePortraitRect, Color.black, Color.grey);
            if (Widgets.ButtonImage(hidePortraitRect, this.hidePortrait ? ShowHidePortraitOff : ShowHidePortraitOn))
            {
                this.hidePortrait = !this.hidePortrait;
            }

            return hidePortraitRect;
        }

        public static Dictionary<Pawn, Dictionary<PortraitElementDef, RenderTexture>> cachedRenderTextures = new();

        public static float zoomValue = 2.402f;
        public static float zOffset = -0.086f;
        public List<(PortraitElementDef, Texture)> GetPortraitTextures()
        {
            List<(PortraitElementDef, Texture)> allTextures = new ();
            List<PortraitLayerDef> resolvedLayers = new List<PortraitLayerDef>();
            bool noMiddleHair = false;
            foreach (var layer in PortraitUtils.layers)
            {
                if (resolvedLayers.Contains(layer))
                    continue;
                if (PortraitUtils.portraitElements.TryGetValue(layer, out var elements) && elements.Any())
                {
                    var matchingElements = elements.Where(x => x.Matches(this)).ToList();
                    if (this.currentStyle.NullOrEmpty() is false && matchingElements.Any(x => x.requirements.style.NullOrEmpty() is false))
                    {
                        matchingElements = matchingElements.Where(x => x.requirements.style == this.currentStyle).ToList();
                    }
                    if (matchingElements.Any())
                    {
                        if (layer.acceptAllMatchingElements)
                        {
                            foreach (var matchingElement in matchingElements)
                            {
                                GetTexture(allTextures, matchingElement);
                            }
                        }
                        else
                        {
                            GetTextureFrom(allTextures, matchingElements);
                        }
                    }
                    else if (PortraitsOfTheRimSettings.randomizeFaceAndHairAssetsInPlaceOfMissingAssets)
                    {
                        // Temporarily disabling randominzation of head and neck. Some issues with randomization
                        // where human necks may have insectoid heads, etc.
                        /*
                        if (layer == PR_DefOf.PR_Head || layer == PR_DefOf.PR_Neck)
                        {
                            GetTextureFrom(allTextures, elements);
                        }
                        */
                        if (layer == PR_DefOf.PR_InnerFace)
                        {
                            var pickedElement = GetTextureFrom(allTextures, elements);
                            var outerFace = DefDatabase<PortraitElementDef>.GetNamedSilentFail(pickedElement.defName.Replace(layer.defName,
                                PR_DefOf.PR_OuterFace.defName));
                            if (outerFace != null)
                            {
                                GetTexture(allTextures, outerFace);
                                resolvedLayers.Add(PR_DefOf.PR_OuterFace);
                            }
                        }
                        else if (layer == PR_DefOf.PR_MiddleHair)
                        {
                            noMiddleHair = true;
                        }
                        else if (layer == PR_DefOf.PR_OuterHair)
                        {
                            if (noMiddleHair)
                            {
                                var pickedElement = GetTextureFrom(allTextures, elements);
                                var middleHairs = new List<PortraitElementDef>();
                                var baseName = new Regex("(.*)-(.*)").Replace(pickedElement.defName, "$1").Replace(layer.defName, PR_DefOf.PR_MiddleHair.defName);
                                var postfix = new Regex("(.*)-(.*)").Replace(pickedElement.defName, "$2");
                                foreach (var suffix in TextureParser.allSuffices)
                                {
                                    var newDefName = baseName + "-" + suffix + "-" + postfix;
                                    var def = DefDatabase<PortraitElementDef>.GetNamedSilentFail(newDefName);
                                    if (def != null)
                                    {
                                        middleHairs.Add(def);
                                    }
                                }
                                if (middleHairs.Any())
                                {
                                    var middleHair = middleHairs.FirstOrDefault(x => x.requirements.ageRange is null
                                    || x.requirements.ageRange.Value.Includes(pawn.ageTracker.AgeBiologicalYearsFloat));
                                    if (middleHair != null)
                                    {
                                        GetTexture(allTextures, middleHair);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return allTextures;
        }

        private PortraitElementDef GetTextureFrom(List<(PortraitElementDef, Texture)> allTextures, List<PortraitElementDef> elements)
        {
            Rand.PushState();
            Rand.Seed = pawn.thingIDNumber;
            var element = elements.RandomElement();
            Rand.PopState();
            GetTexture(allTextures, element);
            return element;
        }

        private void GetTexture(List<(PortraitElementDef, Texture)> allTextures, PortraitElementDef matchingElement)
        {
            var mainTexture = matchingElement.graphic.MatSingle.mainTexture;
            if (!cachedRenderTextures.TryGetValue(pawn, out var dict))
            {
                cachedRenderTextures[pawn] = dict = new Dictionary<PortraitElementDef, RenderTexture>();
            }
            if (!dict.TryGetValue(matchingElement, out var renderTexture))
            {
                dict[matchingElement] = renderTexture = new RenderTexture(mainTexture.width, mainTexture.height, 0);
            }
            renderTexture.RenderElement(matchingElement, pawn, new Vector3(0, 0, zOffset), zoomValue);
            renderTexture.name = matchingElement.defName;
            allTextures.Add((matchingElement, renderTexture));
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref hidePortrait, "hidePortrait", !PortraitsOfTheRimSettings.showPortraitByDefault);
            Scribe_Values.Look(ref hideHeadgear, "hideHeadgear");
            Scribe_Values.Look(ref currentStyle, "currentStyle");
        }
    }
}
