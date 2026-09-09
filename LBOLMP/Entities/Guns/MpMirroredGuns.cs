using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using LBoL.ConfigData;

namespace LBOLMP.Entities.Guns
{
    /// <summary>
    /// Copies an enemy's gun so a card can shoot it the right way.
    /// </summary>
    internal static class MpMirroredGuns
    {
        // Holy fucking overengineered
        private static readonly HashSet<int> AimedAtTarget = new HashSet<int> { 0, 2, 3, 5 };
        private static readonly HashSet<int> AngleEvents = new HashSet<int> { 2, 4 };
        private static readonly HashSet<int> SidewaysEvents = new HashSet<int> { 11, 16, 18 };
        private const int TowardsValue = 1;
        private const int TimesValue = 2;
        private static float Reflect(float angle) => 180f - angle;
        private static float Turn(float angle) => -angle;
        private static float Flip(float x) => -x;
        private const int PiecesPerGun = 100;

        internal static string OfEnemy(string enemyId, string mirroredName)
        {
            var guns = EnemyUnitConfig.FromId(enemyId)?.Gun1;
            if (guns == null || guns.Count == 0)
            {
                MpPlugin.Log.LogWarning($"'{enemyId}' has no gun to borrow");
                return null;
            }

            string sourceName = guns[0];

            return Mirror(sourceName, mirroredName) ?? sourceName;
        }

        /// <summary>
        /// Registers a mirrored copy of a gun. Idempotent, and null if the source is missing.
        /// </summary>
        private static string Mirror(string sourceName, string mirroredName)
        {
            if (GunConfig.FromName(mirroredName) != null)
            {
                return mirroredName;
            }

            var source = GunConfig.FromName(sourceName);
            if (source == null)
            {
                MpPlugin.Log.LogWarning($"No gun named '{sourceName}' to mirror");
                return null;
            }

            var sourcePieces = PiecesOf(source.Id);
            if (sourcePieces.Count == 0)
            {
                MpPlugin.Log.LogWarning($"Gun '{sourceName}' has no pieces, so there is nothing to mirror");
                return null;
            }


            int mirroredId = GunConfig.AllConfig().Max(gun => gun.Id) + 1;

            var gunCopy = Clone(source);
            gunCopy.Id = mirroredId;
            gunCopy.Name = mirroredName;

            var pieceCopies = new List<PieceConfig>();
            for (int i = 0; i < sourcePieces.Count; i++)
            {
                pieceCopies.Add(MirrorPiece(sourcePieces[i], source.Id, mirroredId, i));
            }

            Register(gunCopy, pieceCopies);
            return mirroredName;
        }

        private static List<PieceConfig> PiecesOf(int gunId)
        {
            var found = new List<PieceConfig>();
            for (int i = 0; i < PiecesPerGun; i++)
            {
                var piece = PieceConfig.FromId(gunId * PiecesPerGun + i);
                if (piece == null)
                {
                    break;
                }

                found.Add(piece);
            }

            return found;
        }

        private static PieceConfig MirrorPiece(PieceConfig source, int sourceGunId, int mirroredGunId, int index)
        {
            var copy = Clone(source);
            copy.Id = mirroredGunId * PiecesPerGun + index;

            copy.ParentPiece = Rebase(source.ParentPiece, sourceGunId, mirroredGunId);
            copy.FollowPiece = Rebase(source.FollowPiece, sourceGunId, mirroredGunId);


            bool aimed = AimedAtTarget.Contains(source.Aim);
            copy.GAngle = Map(source.GAngle, angle => aimed ? Turn(angle) : Reflect(angle));

            copy.RadiusA = Map(source.RadiusA, Turn);


            copy.StartAccAngle = Map(source.StartAccAngle, Reflect);

            copy.X = Map(source.X, Flip);

            copy.EvNumber = MirrorEvents(source);

            return copy;
        }

        private static float[][][] MirrorEvents(PieceConfig source)
        {
            var numbers = source.EvNumber;
            var types = source.EvType;
            if (numbers == null || types == null)
            {
                return numbers;
            }

            var copy = new float[numbers.Length][][];
            for (int i = 0; i < numbers.Length; i++)
            {
                copy[i] = i < types.Length
                    ? Map(numbers[i], ChangeFor(types[i]))
                    : numbers[i];
            }

            return copy;
        }

        private static Func<float, float> ChangeFor(int[] type)
        {
            if (type == null || type.Length == 0)
            {
                return null;
            }

            int mode = type.Length > 1 ? type[1] : 0;
            if (mode == TimesValue)
            {
                return null;
            }

            if (AngleEvents.Contains(type[0]))
            {
                return mode == TowardsValue ? (Func<float, float>)Reflect : Turn;
            }

            return SidewaysEvents.Contains(type[0]) ? (Func<float, float>)Flip : null;
        }

        private static int Rebase(int pieceId, int sourceGunId, int mirroredGunId)
        {
            int sourceBase = sourceGunId * PiecesPerGun;

            // 0 is "none" rather than the first piece of gun 0.
            if (pieceId < sourceBase || pieceId >= sourceBase + PiecesPerGun)
            {
                return pieceId;
            }

            return mirroredGunId * PiecesPerGun + (pieceId - sourceBase);
        }

        private static float[][] Map(float[][] source, Func<float, float> change)
        {
            if (source == null || change == null)
            {
                return source;
            }

            var copy = new float[source.Length][];
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] == null)
                {
                    continue;
                }

                copy[i] = (float[])source[i].Clone();
                if (copy[i].Length > 0)
                {
                    copy[i][0] = i == 0 ? change(copy[i][0]) : Turn(copy[i][0]);
                }
            }

            return copy;
        }

        private static T Clone<T>(T source) where T : class
        {
            var copy = (T)FormatterServices.GetUninitializedObject(typeof(T));
            foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.CanRead && property.CanWrite)
                {
                    property.SetValue(copy, property.GetValue(source));
                }
            }

            return copy;
        }

        private static void Register(GunConfig gun, List<PieceConfig> pieces)
        {
            GunConfig._data = GunConfig._data.Concat(new[] { gun }).ToArray();
            GunConfig._NameTable[gun.Name] = gun;

            PieceConfig._data = PieceConfig._data.Concat(pieces).ToArray();
            foreach (var piece in pieces)
            {
                PieceConfig._IdTable[piece.Id] = piece;
            }

            MpPlugin.Log.LogInfo($"Registered mirrored gun '{gun.Name}'");
        }
    }
}
