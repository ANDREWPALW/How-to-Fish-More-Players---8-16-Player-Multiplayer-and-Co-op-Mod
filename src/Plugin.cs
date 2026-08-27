using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace HowToFishMorePlayers
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "andrewpalww.howtofish.moreplayers";
        public const string PluginName = "How To Fish More Players";
        public const string PluginVersion = "2.1.1";
        public const int TargetPlayers = 16;

        private Harmony _harmony;
        private static BepInEx.Logging.ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{PluginName} v{PluginVersion} loading. Target player limit: {TargetPlayers}.");

            try
            {
                _harmony = new Harmony(PluginGuid);
                int installed = 0;

                // These are the two actual hard-coded player-count sites in How To Fish:
                // SteamManager.CreateLobby(): SteamMatchmaking.CreateLobby(..., 8)
                // SteamManager.OnLobbyCreated(): ConnectionManager.CreateOnlineLobby(..., 8)
                installed += PatchConstantEight("SteamManager", "CreateLobby");
                installed += PatchConstantEight("SteamManager", "OnLobbyCreated");

                // Failsafes. They keep later calls from restoring the original 8-player cap.
                installed += PatchNamedMethodPrefix("ConnectionManager", "CreateOnlineLobby", nameof(ForceIntegerArguments));
                installed += PatchSteamMatchmaking();
                installed += PatchFishySteamworks();

                Log.LogInfo($"{PluginName}: installed {installed} Harmony patches.");
                Log.LogInfo("16-player limit is active for the Steam lobby and FishNet/FishySteamworks transport.");
            }
            catch (Exception ex)
            {
                Log.LogError($"Fatal error while installing patches: {ex}");
            }
        }

        private void OnDestroy()
        {
            try
            {
                _harmony?.UnpatchSelf();
            }
            catch
            {
                // Game is shutting down; nothing useful to do here.
            }
        }

        private int PatchConstantEight(string typeName, string methodName)
        {
            Type type = FindType(typeName);
            if (type == null)
            {
                Log.LogWarning($"Could not find {typeName}; {methodName} was not patched.");
                return 0;
            }

            MethodInfo method = AccessTools.Method(type, methodName);
            if (method == null)
            {
                Log.LogWarning($"Could not find {typeName}.{methodName}().");
                return 0;
            }

            var transpiler = new HarmonyMethod(typeof(Plugin).GetMethod(
                nameof(ReplaceEightWithSixteen), BindingFlags.Static | BindingFlags.NonPublic));

            _harmony.Patch(method, transpiler: transpiler);
            Log.LogInfo($"Patched {type.FullName}.{method.Name}(): hard-coded 8 -> {TargetPlayers}.");
            return 1;
        }

        private int PatchNamedMethodPrefix(string typeName, string methodName, string prefixName)
        {
            Type type = FindType(typeName);
            if (type == null)
                return 0;

            int count = 0;
            MethodInfo prefixInfo = typeof(Plugin).GetMethod(prefixName, BindingFlags.Static | BindingFlags.NonPublic);
            var prefix = new HarmonyMethod(prefixInfo);

            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name != methodName || method.IsAbstract)
                    continue;

                try
                {
                    _harmony.Patch(method, prefix: prefix);
                    Log.LogInfo($"Patched {type.FullName}.{Describe(method)}.");
                    count++;
                }
                catch (Exception ex)
                {
                    Log.LogWarning($"Failed to patch {type.FullName}.{Describe(method)}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            return count;
        }

        private int PatchSteamMatchmaking()
        {
            Type steamMatchmaking = FindType("Steamworks.SteamMatchmaking");
            if (steamMatchmaking == null)
            {
                Log.LogWarning("Steamworks.SteamMatchmaking was not found. The two direct game patches remain active.");
                return 0;
            }

            int count = 0;
            MethodInfo prefixInfo = typeof(Plugin).GetMethod(nameof(ForceIntegerArguments), BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo postfixInfo = typeof(Plugin).GetMethod(nameof(ForceIntegerResult), BindingFlags.Static | BindingFlags.NonPublic);
            var prefix = new HarmonyMethod(prefixInfo);
            var postfix = new HarmonyMethod(postfixInfo);

            foreach (MethodInfo method in steamMatchmaking.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                bool patchPrefix = method.Name == "CreateLobby" || method.Name == "SetLobbyMemberLimit";
                bool patchPostfix = method.Name == "GetLobbyMemberLimit" && method.ReturnType == typeof(int);
                if (!patchPrefix && !patchPostfix)
                    continue;

                try
                {
                    _harmony.Patch(method,
                        prefix: patchPrefix ? prefix : null,
                        postfix: patchPostfix ? postfix : null);
                    Log.LogInfo($"Patched {steamMatchmaking.FullName}.{Describe(method)}.");
                    count++;
                }
                catch (Exception ex)
                {
                    Log.LogWarning($"Failed to patch {steamMatchmaking.FullName}.{Describe(method)}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            return count;
        }

        private int PatchFishySteamworks()
        {
            int count = 0;
            MethodInfo prefixInfo = typeof(Plugin).GetMethod(nameof(ForceIntegerArguments), BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo postfixInfo = typeof(Plugin).GetMethod(nameof(ForceIntegerResult), BindingFlags.Static | BindingFlags.NonPublic);
            var prefix = new HarmonyMethod(prefixInfo);
            var postfix = new HarmonyMethod(postfixInfo);

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (Type type in GetTypesSafe(assembly))
                {
                    string fullName = type.FullName ?? string.Empty;
                    if (type.IsInterface || fullName.IndexOf("FishySteamworks", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (method.IsAbstract)
                            continue;

                        bool patchPrefix = method.Name == "SetMaximumClients" || method.Name == "StartConnection";
                        bool patchPostfix = method.Name == "GetMaximumClients" && method.ReturnType == typeof(int);
                        if (!patchPrefix && !patchPostfix)
                            continue;

                        // Only touch methods which actually expose an Int32 player-limit argument.
                        if (patchPrefix && !method.GetParameters().Any(p => p.ParameterType == typeof(int)))
                            continue;

                        try
                        {
                            _harmony.Patch(method,
                                prefix: patchPrefix ? prefix : null,
                                postfix: patchPostfix ? postfix : null);
                            Log.LogInfo($"Patched {type.FullName}.{Describe(method)}.");
                            count++;
                        }
                        catch (Exception ex)
                        {
                            Log.LogWarning($"Failed to patch {type.FullName}.{Describe(method)}: {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                }
            }

            return count;
        }

        private static IEnumerable<CodeInstruction> ReplaceEightWithSixteen(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (IsLoadInt32(instruction, 8))
                {
                    instruction.opcode = OpCodes.Ldc_I4_S;
                    instruction.operand = (sbyte)TargetPlayers;
                    replacements++;
                }

                yield return instruction;
            }

            if (replacements == 0)
                Log?.LogWarning($"No literal 8 was found in {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name}.");
            else
                Log?.LogInfo($"{__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name}: replaced {replacements} player-limit literal(s) with {TargetPlayers}.");
        }

        private static bool IsLoadInt32(CodeInstruction instruction, int value)
        {
            if (value == 8 && instruction.opcode == OpCodes.Ldc_I4_8)
                return true;

            if (instruction.opcode == OpCodes.Ldc_I4_S && instruction.operand is sbyte sb)
                return sb == value;

            if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int i)
                return i == value;

            return false;
        }

        // Mono/BepInEx 5: Harmony's __args is safe here and lets the same failsafe patch
        // cover Steamworks.NET and multiple FishySteamworks versions without hard references.
        private static void ForceIntegerArguments(MethodBase __originalMethod, object[] __args)
        {
            if (__args == null)
                return;

            ParameterInfo[] parameters = __originalMethod.GetParameters();
            bool changed = false;

            for (int i = 0; i < __args.Length && i < parameters.Length; i++)
            {
                if (parameters[i].ParameterType != typeof(int) || !LooksLikePlayerLimit(__originalMethod, parameters[i], i))
                    continue;

                int current = __args[i] is int value ? value : 0;
                if (current != TargetPlayers)
                {
                    __args[i] = TargetPlayers;
                    changed = true;
                    Log?.LogInfo($"FORCED {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name} int arg #{i}: {current} -> {TargetPlayers}.");
                }
            }

            if (!changed && __originalMethod.Name == "CreateOnlineLobby")
                Log?.LogDebug($"{__originalMethod.Name}: player limit already {TargetPlayers}.");
        }

        private static bool LooksLikePlayerLimit(MethodBase method, ParameterInfo parameter, int index)
        {
            string methodName = method.Name ?? string.Empty;
            string parameterName = parameter.Name ?? string.Empty;

            if (methodName == "SetMaximumClients")
                return true;

            if (methodName == "CreateOnlineLobby")
                return index == 1 || parameterName.IndexOf("max", StringComparison.OrdinalIgnoreCase) >= 0 || parameterName.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0;

            if (methodName == "CreateLobby" || methodName == "SetLobbyMemberLimit")
                return index == method.GetParameters().Length - 1 || parameterName.IndexOf("max", StringComparison.OrdinalIgnoreCase) >= 0 || parameterName.IndexOf("member", StringComparison.OrdinalIgnoreCase) >= 0;

            if (methodName == "StartConnection")
                return parameterName.IndexOf("max", StringComparison.OrdinalIgnoreCase) >= 0 || parameterName.IndexOf("client", StringComparison.OrdinalIgnoreCase) >= 0;

            return false;
        }

        private static void ForceIntegerResult(MethodBase __originalMethod, ref int __result)
        {
            if (__result < TargetPlayers)
            {
                int old = __result;
                __result = TargetPlayers;
                Log?.LogInfo($"FORCED {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name} result: {old} -> {TargetPlayers}.");
            }
        }

        private static Type FindType(string fullOrSimpleName)
        {
            Type type = AccessTools.TypeByName(fullOrSimpleName);
            if (type != null)
                return type;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (Type candidate in GetTypesSafe(assembly))
                {
                    if (candidate.FullName == fullOrSimpleName || candidate.Name == fullOrSimpleName)
                        return candidate;
                }
            }

            return null;
        }

        private static IEnumerable<Type> GetTypesSafe(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null);
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }

        private static string Describe(MethodInfo method)
        {
            string parameters = string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name));
            return $"{method.Name}({parameters})";
        }
    }
}
