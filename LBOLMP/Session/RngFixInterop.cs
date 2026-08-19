using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LBoL.Base;
using LBoL.Core;

namespace LBOLMP.Session
{
    /// <summary>
    /// Gives each player their own reward rolls again, on the streams RngFix took over.
    /// This mod is expected to be run with RngFix, but this necessitates some extra patching to make sure players still see random rewards between one another
    /// (which is something that RngFix would actively prevent, by design)
    internal static class RngFixInterop
    {
        private const string GrRngsTypeName = "RngFix.CustomRngs.GrRngs";
        private const string ShopRngsTypeName = "RngFix.CustomRngs.ShopRngs";

        /// <summary>Plain RandomGen fields on PersRngs that decide what one player is offered.</summary>
        private static readonly string[] PersonalFields =
        {
            "rareExhibitQueueRng",
            "qingeUpgradeQueueRng",
            "cardUpgradeQueueRng",
            "extraCardRewardRng",
            "eliteCardRng",
            "bossCardRng"
        };

        private static bool _resolved;
        private static MethodInfo _getOrCreate;
        private static FieldInfo _persRngsField;
        private static MethodInfo _shopRngsInit;

        public static bool Installed { get; private set; }

        private static void Resolve()
        {
            if (_resolved)
            {
                return;
            }
            _resolved = true;

            Type grRngs = null;
            Type shopRngs = null;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (grRngs == null)
                {
                    grRngs = assembly.GetType(GrRngsTypeName, false);
                }
                if (shopRngs == null)
                {
                    shopRngs = assembly.GetType(ShopRngsTypeName, false);
                }
                if (grRngs != null && shopRngs != null)
                {
                    break;
                }
            }

            if (grRngs == null)
            {
                MpPlugin.Log.LogInfo("RngFix is not installed! The vanilla reward streams are the only ones to personalise. Please consider enabling RngFix!");
                return;
            }

            _getOrCreate = grRngs.GetMethod("GetOrCreate", BindingFlags.Public | BindingFlags.Static);
            _persRngsField = grRngs.GetField("persRngs", BindingFlags.Public | BindingFlags.Instance);
            _shopRngsInit = shopRngs?.GetMethod("Init", BindingFlags.Public | BindingFlags.Static);

            if (_getOrCreate == null || _persRngsField == null)
            {
                MpPlugin.Log.LogWarning(
                    "RngFix is installed but its RNG container does not look the way this expects! " +
                    "Reward rolls may be identical for every player");
                return;
            }

            Installed = true;
        }

        /// <summary>
        /// Re-seed RngFix's personal streams for this player.
        /// </summary>
        public static void Personalise(GameRunController gameRun, ulong salt)
        {
            Resolve();
            if (!Installed || gameRun == null)
            {
                return;
            }

            var container = _getOrCreate.Invoke(null, new object[] { gameRun });
            var persRngs = _persRngsField.GetValue(container);
            if (persRngs == null)
            {
                return;
            }

            var type = persRngs.GetType();
            int reseeded = 0;

            foreach (var name in PersonalFields)
            {
                var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (field == null || field.FieldType != typeof(RandomGen))
                {
                    MpPlugin.Log.LogWarning($"RngFix has no '{name}' stream any more; leaving it alone");
                    continue;
                }

                field.SetValue(persRngs, new RandomGen(Mix(salt, name)));
                reseeded++;
            }

            reseeded += ReseedShop(persRngs, type, salt) ? 1 : 0;
            reseeded += ReseedSelfRngs(persRngs, type, "exhibitSelfRngs", salt) ? 1 : 0;

            int expected = PersonalFields.Length + 2;
            if (reseeded == expected)
            {
                MpPlugin.Log.LogInfo(
                    $"Personalised all {expected} of RngFix's reward streams for this player");
            }
            else
            {
                MpPlugin.Log.LogWarning(
                    $"Personalised only {reseeded} of RngFix's {expected} reward streams; " +
                    "the rest will be identical for every player");
            }
        }

        private static bool ReseedShop(object persRngs, Type type, ulong salt)
        {
            var field = type.GetField("shopRngs", BindingFlags.Public | BindingFlags.Instance);
            if (field == null || _shopRngsInit == null)
            {
                return false;
            }

            field.SetValue(persRngs, _shopRngsInit.Invoke(null, new object[] { Mix(salt, "shopRngs") }));
            return true;
        }

        private static bool ReseedSelfRngs(object persRngs, Type type, string fieldName, ulong salt)
        {
            var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            var holder = field?.GetValue(persRngs);
            if (holder == null)
            {
                return false;
            }

            var holderType = holder.GetType();

            var rootField = holderType.GetField("rootRng", BindingFlags.Public | BindingFlags.Instance);
            if (rootField != null && rootField.FieldType == typeof(RandomGen))
            {
                rootField.SetValue(holder, new RandomGen(Mix(salt, fieldName)));
            }

            if (!(holderType.GetField("rngs", BindingFlags.Public | BindingFlags.Instance)?.GetValue(holder)
                    is IDictionary map))
            {
                return false;
            }

            foreach (var key in map.Keys.Cast<object>().ToList())
            {
                map[key] = new RandomGen(Mix(salt, fieldName + ":" + key));
            }

            return true;
        }

        private static ulong Mix(ulong salt, string streamName)
        {
            ulong hash = 14695981039346656037UL;
            foreach (char c in streamName)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }

            ulong seed = salt ^ hash;
            return seed == 0 ? 1UL : seed;
        }
    }
}
