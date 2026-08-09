using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Bootstrap;
using HarmonyLib;
using UnityEngine;

public sealed partial class ElinModifierPlugin
{
    private void RecordAiInterruptedContext(string originalPrompt, string stage, string toolResults, string partialResponse, string errorText)
    {
        if (string.IsNullOrWhiteSpace(originalPrompt))
            return;

        var sb = new StringBuilder();
        sb.AppendLine("AI operation interrupted before completion.");
        if (!string.IsNullOrWhiteSpace(stage))
            sb.AppendLine("Stage: " + stage);
        if (!string.IsNullOrWhiteSpace(errorText))
            sb.AppendLine("Interruption reason: " + errorText);
        if (!string.IsNullOrWhiteSpace(toolResults))
        {
            sb.AppendLine();
            sb.AppendLine("Tool results before interruption:");
            sb.AppendLine(toolResults.Trim());
        }
        if (!string.IsNullOrWhiteSpace(partialResponse))
        {
            sb.AppendLine();
            sb.AppendLine("Partial assistant response before interruption:");
            sb.AppendLine(partialResponse.Trim());
        }

        _aiMessages.Add(new AiChatMessage("user", originalPrompt));
        _aiMessages.Add(new AiChatMessage("assistant", sb.ToString().TrimEnd()));
    }
    private void AppendAiTranscriptBlock(string zhLabel, string enLabel, string content)
    {
        AppendAiTranscriptHeader(zhLabel, enLabel);
        if (!string.IsNullOrEmpty(content))
            _aiResponse += content;
    }
    private void AppendAiTranscriptHeader(string zhLabel, string enLabel)
    {
        if (!string.IsNullOrEmpty(_aiResponse))
            _aiResponse += "\n\n";
        _aiResponse += T(zhLabel, enLabel) + ": " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\n";
    }
    private void RunAiTask(Func<Task<string>> request, Action<string> onSuccess, Action<Exception> onError, Action onFinally)
    {
        StartCoroutine(RunAiTaskCoroutine(request, onSuccess, onError, onFinally));
    }
    private IEnumerator RunAiTaskCoroutine(Func<Task<string>> request, Action<string> onSuccess, Action<Exception> onError, Action onFinally)
    {
        Task<string> task;
        try
        {
            task = request();
        }
        catch (Exception ex)
        {
            onError(ex);
            onFinally();
            yield break;
        }

        while (!task.IsCompleted)
            yield return null;

        try
        {
            if (task.IsFaulted)
                throw task.Exception != null && task.Exception.InnerException != null ? task.Exception.InnerException : task.Exception ?? new Exception("Unknown request error");
            if (task.IsCanceled)
                throw new OperationCanceledException("Request canceled");
            onSuccess(task.Result);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            onError(ex);
        }
        finally
        {
            onFinally();
        }
    }
    private void RunAiStreamTask(Func<Task<AiStreamResult>> request, Action<AiStreamResult> onSuccess, Action<Exception> onError, Action onFinally)
    {
        StartCoroutine(RunAiStreamTaskCoroutine(request, onSuccess, onError, onFinally));
    }
    private IEnumerator RunAiStreamTaskCoroutine(Func<Task<AiStreamResult>> request, Action<AiStreamResult> onSuccess, Action<Exception> onError, Action onFinally)
    {
        Task<AiStreamResult> task;
        try
        {
            task = request();
        }
        catch (Exception ex)
        {
            onError(ex);
            onFinally();
            yield break;
        }

        while (!task.IsCompleted)
            yield return null;

        try
        {
            if (task.IsFaulted)
                throw task.Exception != null && task.Exception.InnerException != null ? task.Exception.InnerException : task.Exception ?? new Exception("Unknown request error");
            if (task.IsCanceled)
                throw new OperationCanceledException("Request canceled");
            onSuccess(task.Result);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            onError(ex);
        }
        finally
        {
            onFinally();
        }
    }
    private async Task<string> AiGetAsync(string url, string apiKey, int timeoutSeconds, CancellationToken cancellationToken)
    {
        using (var requestCancellation = _modules.AiHttpTransport.CreateRequestCancellation(
                   cancellationToken, timeoutSeconds, AiHttpTimeoutMinSeconds, AiHttpTimeoutMaxSeconds))
        using (var request = _modules.AiHttpTransport.CreateRequest(HttpMethod.Get, url, apiKey))
        using (var response = await _modules.AiHttpTransport.Client.SendAsync(
                   request, HttpCompletionOption.ResponseContentRead, requestCancellation.Token).ConfigureAwait(false))
        {
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new Exception(((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + " " + response.ReasonPhrase + ": " + TruncateForLog(text, 500));
            return text;
        }
    }
    private async Task<AiStreamResult> AiPostStreamAsync(string url, string apiKey, string body, int timeoutSeconds, CancellationToken cancellationToken, Action<string> onDelta)
    {
        using (var requestCancellation = _modules.AiHttpTransport.CreateRequestCancellation(
                   cancellationToken, timeoutSeconds, AiHttpTimeoutMinSeconds, AiHttpTimeoutMaxSeconds))
        using (var request = _modules.AiHttpTransport.CreateRequest(HttpMethod.Post, url, apiKey))
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using (var response = await _modules.AiHttpTransport.Client.SendAsync(
                       request, HttpCompletionOption.ResponseHeadersRead, requestCancellation.Token).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    var errorText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    throw new Exception(((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + " " + response.ReasonPhrase + ": " + TruncateForLog(errorText, 500));
                }

                var raw = new StringBuilder();
                var text = new StringBuilder();
                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string? line;
                    while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                    {
                        requestCancellation.Token.ThrowIfCancellationRequested();
                        raw.AppendLine(line);
                        var data = ExtractAiStreamData(line);
                        if (data == null)
                            continue;
                        if (string.Equals(data, "[DONE]", StringComparison.OrdinalIgnoreCase))
                            break;
                        var delta = ExtractAiStreamDelta(data);
                        if (string.IsNullOrEmpty(delta))
                            continue;
                        text.Append(delta);
                        if (onDelta != null)
                            onDelta(delta);
                    }
                }

                var responseText = text.ToString();
                var rawText = raw.ToString();
                if (string.IsNullOrEmpty(responseText))
                    responseText = ExtractAiChatContent(rawText);
                if (string.IsNullOrEmpty(responseText))
                    responseText = rawText;
                return new AiStreamResult(responseText, rawText);
            }
        }
    }
    private async Task<string> AiPostAsync(string url, string apiKey, string body, int timeoutSeconds, CancellationToken cancellationToken)
    {
        using (var requestCancellation = _modules.AiHttpTransport.CreateRequestCancellation(
                   cancellationToken, timeoutSeconds, AiHttpTimeoutMinSeconds, AiHttpTimeoutMaxSeconds))
        using (var request = _modules.AiHttpTransport.CreateRequest(HttpMethod.Post, url, apiKey))
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using (var response = await _modules.AiHttpTransport.Client.SendAsync(
                       request, HttpCompletionOption.ResponseContentRead, requestCancellation.Token).ConfigureAwait(false))
            {
                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new Exception(((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + " " + response.ReasonPhrase + ": " + TruncateForLog(text, 500));
                return text;
            }
        }
    }
    private static string NormalizeAiApiBase(string apiBase)
    {
        if (apiBase == null)
            return "";
        var value = apiBase.Trim();
        while (value.EndsWith("/", StringComparison.Ordinal))
            value = value.Substring(0, value.Length - 1);
        if (IsAiFullEndpoint(value))
            return value;
        if (value.IndexOf("/v1", StringComparison.OrdinalIgnoreCase) < 0)
            value += "/v1";
        return value;
    }
    private static string BuildAiEndpoint(string apiBase, string relativePath)
    {
        apiBase = NormalizeAiApiBase(apiBase);
        relativePath = relativePath.TrimStart('/');
        if (string.Equals(relativePath, "chat/completions", StringComparison.OrdinalIgnoreCase) && IsAiChatCompletionsEndpoint(apiBase))
            return apiBase;
        if (string.Equals(relativePath, "models", StringComparison.OrdinalIgnoreCase) && IsAiChatCompletionsEndpoint(apiBase))
            return apiBase.Substring(0, apiBase.Length - "chat/completions".Length).TrimEnd('/') + "/models";
        return apiBase + "/" + relativePath;
    }
    private static bool IsAiFullEndpoint(string url)
    {
        return IsAiChatCompletionsEndpoint(url) ||
               EndsWithAiPath(url, "/models");
    }
    private static bool IsAiChatCompletionsEndpoint(string url)
    {
        return EndsWithAiPath(url, "/chat/completions");
    }
    private static bool EndsWithAiPath(string url, string path)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(path))
            return false;
        var value = url.Trim();
        var query = value.IndexOf('?');
        if (query >= 0)
            value = value.Substring(0, query);
        while (value.EndsWith("/", StringComparison.Ordinal))
            value = value.Substring(0, value.Length - 1);
        return value.EndsWith(path, StringComparison.OrdinalIgnoreCase);
    }
    private static string BuildAiChatJson(string model, string prompt, IEnumerable<AiChatMessage> history, bool useContext, bool reasoningEnabled, string reasoningEffort, bool includeTools, bool stream, bool toolStream)
    {
        var sb = new StringBuilder();
        sb.Append("{");
        sb.Append("\"model\":\"").Append(EscapeJson(model)).Append("\",");
        if (reasoningEnabled)
        {
            sb.Append("\"reasoning_effort\":\"").Append(EscapeJson(NormalizeAiReasoningEffort(reasoningEffort))).Append("\",");
        }
        else
        {
            sb.Append("\"thinking\":{\"type\":\"disabled\"},");
        }
        sb.Append("\"stream\":").Append(stream ? "true" : "false").Append(",");
        if (includeTools && toolStream)
            sb.Append("\"tool_stream\":true,");
        sb.Append("\"messages\":[");
        AppendAiMessageJson(sb, "system", BuildAiSystemPrompt(includeTools));
        if (useContext && history != null)
        {
            foreach (var message in history)
            {
                if (message == null || string.IsNullOrEmpty(message.Content))
                    continue;
                sb.Append(",");
                AppendAiMessageJson(sb, NormalizeAiChatRole(message.Role), message.Content);
            }
        }
        sb.Append(",");
        AppendAiMessageJson(sb, "user", prompt);
        sb.Append("]");
        if (includeTools)
        {
            sb.Append(",\"tools\":");
            AppendAiToolDefinitionsJson(sb);
            sb.Append(",\"tool_choice\":\"auto\"");
        }
        sb.Append("}");
        return sb.ToString();
    }
    private static string BuildAiSystemPrompt(bool includeTools)
    {
        var prompt = "You are an assistant embedded in Elin Modifier. Answer in the user's language. " +
                     "When the user asks you to operate the modifier, use EMG (Elin Modifier Gateway). Keep unresolved goals from earlier turns in mind unless the user explicitly changes or cancels them. " +
                     "Historical user messages in the conversation are preserved verbatim and are authoritative requirements; before answering, review them together with the latest user message and carry forward any related requirement that was not explicitly cancelled or completed. " +
                     "Before acting, carefully decompose the user's request into all explicit requirements, constraints, targets, exclusions, and verification needs, then keep that checklist in mind throughout the tool loop. Do not treat a partial result as completion when any related requirement remains unresolved. " +
                     "Never claim an EMG action succeeded unless an EMG result says it succeeded. " +
                     "Do not access or operate Debug mode or AI Assistant settings. EMP plugins in workspace/Plugin are managed with emp_list_plugins, emp_set_function_state, and emp_reload_plugins; use those tools to inspect and apply EMP JSON definitions. " +
                     "Security boundary: never download, retrieve, suggest downloading, execute, or embed network-sourced executable files such as .exe/.scr/.dll payloads, PowerShell or cmd scripts such as .ps1/.bat/.cmd, or documented/known exploitable shellcode or payloads. This boundary must not block normal local EMG use: local game data edits, inventory edits, spawning, reflection, local method invocation, local ILSpy workspace search, EMP plugin management, and local Harmony patches for game/mod logic remain allowed when they do not retrieve external executables/scripts or embed shellcode/payload loaders. If a user request requires a prohibited action, refuse only that unsafe part and continue with allowed local EMG game/mod operations.";
        if (includeTools)
            prompt += " You may use EMG tools, including high-risk runtime reflection, arbitrary runtime method invocation, and Harmony patch tools whenever built-in EMG tools do not directly complete the user's request. " +
                      "When generating runtime_harmony_patch code, do not include network download logic, shell launch logic, external executable/script retrieval, shellcode payloads, or encoded/encrypted payload loaders; ordinary local Prefix/Postfix code that edits game state or calls local game/mod methods is still allowed. " +
                      "EMG tool selection rules: use runtime_list_assemblies only to discover loaded assemblies or decompiled caches; use runtime_search/runtime_list_type to discover types, fields, properties, and methods; use runtime_reflect_get only for a concrete readable field/property expression written as a dot path such as EClass.pc.idFaith or nearby_npc.faith; use runtime_harmony_patch only after a concrete method target is known. runtime_harmony_patch is also the built-in code patch entry: for generated Prefix/Postfix logic, call runtime_harmony_patch directly with mode=prefix_code or mode=postfix_code and a code argument. " +
                      "There is no separate runtime_list_code, runtime_patch, code_inject, PatchGenCns, or PatchGen tool. Never suggest those names; use runtime_search/runtime_list_type for source discovery and runtime_harmony_patch for patch installation. " +
                      "Never call runtime_reflect_get with an assembly-only, type-only, or method target such as Assembly:Elin, Type:GameDate, or Assembly:Elin:GameDate.AdvanceMin. " +
                      "Never use colon labels for object members in runtime_reflect_get: nearby_npc: faith is invalid; use nearby_npc.faith. Dictionary/list/array reads use square brackets, for example EClass.sources.religions.map[\"harmony\"].Name or someList[0]. " +
                      "High-risk runtime EMG tools do not execute immediately: they create a pending action that requires the user to confirm in the UI before it is actually applied. " +
                      "Be aggressive and action-oriented: when the user asks to modify game state, hidden mechanics, or other mods, do not stop at lookup or a capability disclaimer if any runtime path is plausible. Search the ILSpy decompiled workspace first, inspect promising source snippets, then queue runtime_reflect_set, runtime_invoke_method, or runtime_harmony_patch when a plausible implementation point is found. If one target is too narrow or ineffective, continue investigating nearby callers/callees and choose a deeper hook point. " +
                      "For custom Harmony logic, runtime_harmony_patch supports two equal-priority patch logic sources: patch_method can point to an existing static method in a loaded DLL, and code can contain C# Prefix/Postfix patch code compiled at runtime. If writing code yourself, do not stop after source lookup: pass the generated code string to runtime_harmony_patch as code. Choose patch_method or code based on which one directly satisfies the request; do not treat code as a fallback or lower-priority option. " +
                      "Search all content that is meaningfully related to the request, not only the first literal keyword: include exact names, display names, IDs, translated/synonym terms, related systems, nearby callers/callees, data definitions, and likely owner modules. When necessary, broaden or narrow searches across related terms, types, assemblies, and source snippets until every requested part has a plausible answer or action path. " +
                      "If an EMG tool fails, treat the error as diagnostic information and try a different target, instance, method overload, narrower search, broader search, caller/callee, direct field write, method invocation, or Harmony patch before giving up. " +
                      "After modifying real game data with EMG, read the target back when a read tool exists and report the verified value. After installing a runtime patch, say that the patch was installed and whether it was already present, but do not claim the gameplay effect is verified unless you observed or read evidence through EMG. " +
                      "runtime_search uses plugin workspace/Decompare/<dll>.decompare source folders and runs in a background job; if it returns pending, call the same tool again after a moment. If a cache is missing it starts workspace/ILSpy/ilspycmd.exe once in the background; wait for that cache before continuing. Do not repeat the same search if it fails. Broaden or change keywords, and use live=true only for a narrow dynamic search with assembly_filter or type_filter. " +
                      " To modify the amount of an item already in the backpack, first use list_inventory_items when the item UID or exact display name is unknown, then use set_inventory_item_amount; do not use spawn_item for existing inventory stacks." +
                      " To delete, clear, empty, or permanently remove backpack/inventory items, call delete_inventory_items; do not ask for confirmation in plain text unless an EMG result already returned pending_confirmation." +
                      " To resolve in-game display names or IDs for enchantments, traits, feats, skills, spells, items, NPCs, faiths, or religions, use list_game_names before editing or spawning." +
                      " For player faith/religion changes, first resolve a real religion ID with list_game_names category=religions, then call set_player_info with faith_id. If the user gives a god/NPC name, also search religions before deciding it cannot be changed.";
        var skillPrompt = GetAiSkillPromptBlock();
        if (!string.IsNullOrWhiteSpace(skillPrompt))
            prompt += "\n\n" + skillPrompt;
        return prompt;
    }
    private static string GetAiSkillPromptBlock()
    {
        var skillDirectory = Path.Combine(GetAiRuntimeWorkspaceDirectory(), "Skill");
        try
        {
            if (!Directory.Exists(skillDirectory))
                return "";

            var files = Directory.GetFiles(skillDirectory, "*.md", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            var stamp = BuildAiSkillPromptStamp(files);
            lock (AiSkillPromptCacheLock)
            {
                if (string.Equals(_aiSkillPromptCacheStamp, stamp, StringComparison.Ordinal))
                    return _aiSkillPromptCache;
            }

            var prompt = BuildAiSkillPromptBlock(skillDirectory, files);
            lock (AiSkillPromptCacheLock)
            {
                _aiSkillPromptCacheStamp = stamp;
                _aiSkillPromptCache = prompt;
            }
            return prompt;
        }
        catch
        {
            return "";
        }
    }
    private static string BuildAiSkillPromptStamp(string[] files)
    {
        if (files == null || files.Length == 0)
            return "0";

        var sb = new StringBuilder();
        for (var i = 0; i < files.Length; i++)
        {
            var file = files[i];
            try
            {
                var info = new FileInfo(file);
                sb.Append(file)
                    .Append('|')
                    .Append(info.Length.ToString(CultureInfo.InvariantCulture))
                    .Append('|')
                    .Append(info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture))
                    .Append('\n');
            }
            catch
            {
                sb.Append(file).Append("|error\n");
            }
        }
        return sb.ToString();
    }
    private static string BuildAiSkillPromptBlock(string skillDirectory, string[] files)
    {
        if (files == null || files.Length == 0)
            return "";

        var sb = new StringBuilder();
        var totalChars = 0;
        var included = 0;
        sb.AppendLine("Additional user-provided Skill instructions loaded from plugin workspace/Skill markdown files. Follow these instructions when they are relevant, while still obeying the modifier tool rules above.");
        for (var i = 0; i < files.Length; i++)
        {
            var file = files[i];
            string text;
            try
            {
                var info = new FileInfo(file);
                if (!info.Exists || info.Length == 0)
                    continue;
                text = File.ReadAllText(file, Encoding.UTF8);
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(text))
                continue;

            var remaining = AiSkillPromptMaxChars - totalChars;
            if (remaining <= 0)
            {
                sb.AppendLine();
                sb.AppendLine("[Skill loading truncated: total markdown content limit reached.]");
                break;
            }

            var fileLimit = Math.Min(AiSkillPromptMaxFileChars, remaining);
            if (text.Length > fileLimit)
                text = text.Substring(0, fileLimit) + "\n[Skill file truncated: per-file or total content limit reached.]";

            var relativePath = GetAiSkillRelativePath(skillDirectory, file);
            sb.AppendLine();
            sb.AppendLine("[Skill: " + relativePath + "]");
            sb.AppendLine(text.Trim());
            totalChars += text.Length;
            included++;
        }

        return included == 0 ? "" : sb.ToString().TrimEnd();
    }
    private static string GetAiSkillRelativePath(string root, string file)
    {
        try
        {
            var relative = file;
            if (!string.IsNullOrEmpty(root) && file.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                relative = file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        }
        catch
        {
            return Path.GetFileName(file);
        }
    }
    private string GetAiReasoningEffort()
    {
        if (_aiReasoningEffortIndex < 0 || _aiReasoningEffortIndex >= AiReasoningEfforts.Length)
            _aiReasoningEffortIndex = 0;
        return AiReasoningEfforts[_aiReasoningEffortIndex];
    }
    private bool IsAiReasoningEnabled()
    {
        return !string.Equals(GetAiReasoningEffort(), "off", StringComparison.OrdinalIgnoreCase);
    }
    private static int GetAiReasoningEffortIndex(string effort)
    {
        effort = NormalizeAiReasoningEffort(effort);
        for (var i = 0; i < AiReasoningEfforts.Length; i++)
            if (string.Equals(AiReasoningEfforts[i], effort, StringComparison.OrdinalIgnoreCase))
                return i;
        return 0;
    }
    private static string NormalizeAiReasoningEffort(string effort)
    {
        if (string.Equals(effort, "off", StringComparison.OrdinalIgnoreCase))
            return "off";
        if (string.Equals(effort, "low", StringComparison.OrdinalIgnoreCase))
            return "low";
        if (string.Equals(effort, "high", StringComparison.OrdinalIgnoreCase))
            return "high";
        if (string.Equals(effort, "xhigh", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(effort, "extra_high", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(effort, "extra-high", StringComparison.OrdinalIgnoreCase))
            return "xhigh";
        if (string.Equals(effort, "max", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(effort, "maximum", StringComparison.OrdinalIgnoreCase))
            return "max";
        return "medium";
    }
    private static void AppendAiMessageJson(StringBuilder sb, string role, string content)
    {
        sb.Append("{\"role\":\"")
            .Append(EscapeJson(role))
            .Append("\",\"content\":\"")
            .Append(EscapeJson(content))
            .Append("\"}");
    }
    private static void AppendAiToolDefinitionsJson(StringBuilder sb)
    {
        sb.Append("[");
        AppendAiToolDefinition(sb, "set_feature_enabled", "Enable or disable a normal modifier feature. Debug mode and AI Assistant settings are not available.", "feature,enabled",
            "\"feature\":{\"type\":\"string\",\"description\":\"Feature key or display name, such as low_performance_mode, unlock_frame_rate, invincible_mode, invincible_mode_include_party, ignore_buff_effects, ignore_buff_effects_debuff, ignore_buff_effects_buff, ignore_buff_effects_include_party, hostile_threat_marker, show_npc_more_info, show_item_more_info, show_buff_specific_info, show_item_panel_enchant_levels, show_item_panel_item_value, equipment_comparison, experience_multiplier, plant_harvest_multiplier, ignore_crop_growth_conditions, food_restores_sp, dismantle_always_returns_materials, gathering_always_learns_recipe, optimize_melee_hit_chance, optimize_melee_hit_chance_include_party, pc_faction_trainer_all_skills, unlimited_home_resident_cap, unlimited_party_member_cap, unlimited_offering_piety_gain, ignore_god_artifact_faith_requirement, infinite_charge_and_ammo, charge_stacking, right_click_interrupt_operation, steal_hand_no_target_limit, steal_hand_undetectable, merchant_refresh_no_cost, merchant_always_stocks_monster_ball, merchant_monster_ball_level_optimization, ignore_special_npc_hatch_restriction, affinity_only_increase, karma_only_increase, attack_cannot_be_interrupted, attack_cannot_be_interrupted_include_party, fishing_no_wait, gene_synthesis_no_wait, sleep_without_sleepiness, all_purpose_workbench, infinite_sight, show_food_rot, ignore_food_decay, no_material_crafting, unlock_all_crafting_materials, unlock_all_crafting_recipes, custom_item_amount, custom_item_data, custom_food_data, custom_weapon_data, custom_gene_editing, stethoscope_no_target_limit, ignore_terrain_movement, optimize_dungeon_void_scaling, props_list_thing_stack_exception_protection, faction_branch_fix.\"},\"enabled\":{\"type\":\"boolean\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "set_plant_harvest_multiplier_settings", "Set crop harvest and seed reaping multipliers. Values apply only while plant_harvest_multiplier is enabled; 1 preserves vanilla amounts.", "crop_multiplier,seed_multiplier",
            "\"crop_multiplier\":{\"type\":\"number\",\"minimum\":0,\"description\":\"Crop harvest amount multiplier.\"},\"seed_multiplier\":{\"type\":\"number\",\"minimum\":0,\"description\":\"Seed reaping amount multiplier.\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "list_inventory_items", "List real items currently in the player's backpack/inventory with in-game display names, UID, ID, count, level, material, and rarity. Use this before modifying an existing item stack.", "",
            "\"filter\":{\"type\":\"string\",\"description\":\"Optional name, ID, UID, material, or text filter.\"},\"limit\":{\"type\":\"integer\",\"description\":\"Maximum rows to return. Use 0 for all rows. Default 80.\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "set_inventory_item_amount", "Modify the count of an existing real backpack/inventory item stack only. This never spawns a new item. Prefer UID from list_inventory_items; if name/ID is ambiguous, the tool returns candidates instead of changing anything.", "item,count",
            "\"item\":{\"type\":\"string\",\"description\":\"Inventory item UID, exact display name, partial display name, or item ID from list_inventory_items.\"},\"count\":{\"type\":\"integer\",\"description\":\"Target real held amount, must be greater than 0.\"},\"match_mode\":{\"type\":\"string\",\"enum\":[\"auto\",\"uid\",\"name\",\"id\"],\"description\":\"Optional matching mode. Default auto.\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "delete_inventory_items", "High-risk action: permanently delete real backpack/inventory item stacks. This queues a pending confirmation and only executes after the user replies confirm. Use this for clear backpack, delete all inventory, delete matching items, or remove a specific UID.", "scope",
            "\"scope\":{\"type\":\"string\",\"enum\":[\"all\",\"matching\",\"uid\"],\"description\":\"all deletes all editable real backpack items; matching deletes items matching filter; uid deletes one exact UID/item.\"},\"filter\":{\"type\":\"string\",\"description\":\"Required for matching. Name, ID, UID, or text filter.\"},\"item\":{\"type\":\"string\",\"description\":\"Required for uid or a specific item. UID, exact display name, partial display name, or item ID.\"},\"match_mode\":{\"type\":\"string\",\"enum\":[\"auto\",\"uid\",\"name\",\"id\"],\"description\":\"Optional matching mode for item. Default auto.\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "get_inventory_item_data", "Read detailed editable data for a real backpack item, including item/food/weapon/gene fields and current enchantments/effects/gene effects. Use this before detailed edits to avoid overwriting existing values.", "item",
            "\"item\":{\"type\":\"string\",\"description\":\"Inventory item UID, exact display name, partial display name, or item ID from list_inventory_items.\"},\"match_mode\":{\"type\":\"string\",\"enum\":[\"auto\",\"uid\",\"name\",\"id\"],\"description\":\"Optional matching mode. Default auto.\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "set_item_data", "Modify detailed data for an existing real backpack item. Omitted fields keep current values. Enchantments replace the editable enchantment list, so read current data first if you need to preserve entries.", "item",
            "\"item\":{\"type\":\"string\",\"description\":\"Inventory item UID, exact display name, partial display name, or item ID.\"},\"match_mode\":{\"type\":\"string\",\"enum\":[\"auto\",\"uid\",\"name\",\"id\"]},\"level\":{\"type\":\"integer\"},\"enhance\":{\"type\":\"integer\"},\"material_id\":{\"type\":\"integer\"},\"weight\":{\"type\":\"integer\"},\"variant_id\":{\"type\":\"integer\"},\"fixed_price\":{\"type\":\"integer\"},\"value\":{\"type\":\"integer\"},\"value_bonus\":{\"type\":\"integer\"},\"blessed_state\":{\"type\":\"string\",\"description\":\"normal, blessed, cursed, doomed; or 0, 1, -1, -2.\"},\"is_stolen\":{\"type\":\"boolean\"},\"is_crafted\":{\"type\":\"boolean\"},\"is_gifted\":{\"type\":\"boolean\"},\"is_replica\":{\"type\":\"boolean\"},\"is_copy\":{\"type\":\"boolean\"},\"is_fireproof\":{\"type\":\"boolean\"},\"is_acidproof\":{\"type\":\"boolean\"},\"is_broken\":{\"type\":\"boolean\"},\"no_sell\":{\"type\":\"boolean\"},\"is_lost_property\":{\"type\":\"boolean\"},\"rarity\":{\"type\":\"integer\",\"description\":\"-100 poor, 0 standard, 100 superior, 200 miracle, 300 godly, 400 artifact.\"},\"enchantments\":{\"type\":\"string\",\"description\":\"Optional comma/semicolon/newline list of elementId=value pairs. Replaces current item enchantments/effects.\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "set_food_data", "Modify detailed data for an existing real backpack food item. Omitted fields keep current values. Effects replace the editable food effect list, so read current data first if needed.", "item",
            "\"item\":{\"type\":\"string\",\"description\":\"Inventory food UID, exact display name, partial display name, or item ID.\"},\"match_mode\":{\"type\":\"string\",\"enum\":[\"auto\",\"uid\",\"name\",\"id\"]},\"level\":{\"type\":\"integer\"},\"enhance\":{\"type\":\"integer\"},\"material_id\":{\"type\":\"integer\"},\"weight\":{\"type\":\"integer\"},\"rot\":{\"type\":\"integer\"},\"blessed_state\":{\"type\":\"string\",\"description\":\"normal, blessed, cursed, doomed; or 0, 1, -1, -2.\"},\"is_stolen\":{\"type\":\"boolean\"},\"is_crafted\":{\"type\":\"boolean\"},\"is_gifted\":{\"type\":\"boolean\"},\"is_replica\":{\"type\":\"boolean\"},\"is_copy\":{\"type\":\"boolean\"},\"is_fireproof\":{\"type\":\"boolean\"},\"is_acidproof\":{\"type\":\"boolean\"},\"is_broken\":{\"type\":\"boolean\"},\"no_sell\":{\"type\":\"boolean\"},\"is_lost_property\":{\"type\":\"boolean\"},\"rarity\":{\"type\":\"integer\"},\"effects\":{\"type\":\"string\",\"description\":\"Optional comma/semicolon/newline list of elementId=value pairs. Replaces current food effects.\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "set_weapon_data", "Modify detailed data for an existing real backpack weapon/tool. Omitted fields keep current values. Enchantments replace the editable weapon enchantment list, so read current data first if needed.", "item",
            "\"item\":{\"type\":\"string\",\"description\":\"Inventory weapon/tool UID, exact display name, partial display name, or item ID.\"},\"match_mode\":{\"type\":\"string\",\"enum\":[\"auto\",\"uid\",\"name\",\"id\"]},\"level\":{\"type\":\"integer\"},\"enhance\":{\"type\":\"integer\"},\"material_id\":{\"type\":\"integer\"},\"damage_dice_sides\":{\"type\":\"integer\"},\"hit\":{\"type\":\"integer\"},\"damage_bonus\":{\"type\":\"integer\"},\"dv\":{\"type\":\"integer\"},\"pv\":{\"type\":\"integer\"},\"weight\":{\"type\":\"integer\"},\"charges\":{\"type\":\"integer\"},\"ammo\":{\"type\":\"integer\"},\"range\":{\"type\":\"integer\"},\"penetration\":{\"type\":\"integer\"},\"modification_slots\":{\"type\":\"integer\"},\"blessed_state\":{\"type\":\"string\",\"description\":\"normal, blessed, cursed, doomed; or 0, 1, -1, -2.\"},\"is_stolen\":{\"type\":\"boolean\"},\"is_crafted\":{\"type\":\"boolean\"},\"is_gifted\":{\"type\":\"boolean\"},\"is_replica\":{\"type\":\"boolean\"},\"is_copy\":{\"type\":\"boolean\"},\"is_fireproof\":{\"type\":\"boolean\"},\"is_acidproof\":{\"type\":\"boolean\"},\"is_broken\":{\"type\":\"boolean\"},\"no_sell\":{\"type\":\"boolean\"},\"is_lost_property\":{\"type\":\"boolean\"},\"rarity\":{\"type\":\"integer\"},\"enchantments\":{\"type\":\"string\",\"description\":\"Optional comma/semicolon/newline list of elementId=value pairs. Replaces current weapon enchantments.\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "set_gene_data", "Modify detailed data for an existing real backpack gene item. Omitted fields keep current values. Effects replace the gene effect list, so read current data first if needed.", "item",
            "\"item\":{\"type\":\"string\",\"description\":\"Inventory gene UID, exact display name, partial display name, or item ID.\"},\"match_mode\":{\"type\":\"string\",\"enum\":[\"auto\",\"uid\",\"name\",\"id\"]},\"source_id\":{\"type\":\"string\"},\"level\":{\"type\":\"integer\"},\"seed\":{\"type\":\"integer\"},\"cost\":{\"type\":\"integer\"},\"slots\":{\"type\":\"integer\"},\"effects\":{\"type\":\"string\",\"description\":\"Optional comma/semicolon/newline list of elementId=value pairs. Replaces current gene effects.\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "list_game_names", "Read in-game display names and IDs from game data. Supports enchantments, traits, feats, skills, spells, items, NPCs, faiths, and religions. Use filter/limit to avoid huge responses; use limit 0 only when full output is truly needed.", "category",
            "\"category\":{\"type\":\"string\",\"enum\":[\"enchantments\",\"traits\",\"feats\",\"skills\",\"spells\",\"items\",\"npcs\",\"religions\",\"all\"]},\"filter\":{\"type\":\"string\",\"description\":\"Optional display name, alias, ID, category, race, job, faith, or religion filter.\"},\"limit\":{\"type\":\"integer\",\"description\":\"Maximum rows per requested category. Use 0 for all rows. Default 80.\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "spawn_item", "Create a new item stack in the player's inventory. Do not use this for changing the amount of an item already in the backpack; use set_inventory_item_amount instead.", "item_id,count,level,material_id",
            "\"item_id\":{\"type\":\"string\",\"description\":\"Exact item ID, or a name/alias to search.\"},\"count\":{\"type\":\"integer\"},\"level\":{\"type\":\"integer\"},\"material_id\":{\"type\":\"integer\",\"description\":\"Use -1 for default material.\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "spawn_npc", "Spawn an NPC near the player, with affinity and relationship set immediately. If level is omitted or less than 1, the NPC template LV is used.", "npc_id,level,affinity,relationship",
            "\"npc_id\":{\"type\":\"string\",\"description\":\"Exact NPC ID, or a name/race/job to search.\"},\"level\":{\"type\":\"integer\",\"description\":\"Use -1 or omit to use the NPC template LV.\"},\"affinity\":{\"type\":\"integer\"},\"relationship\":{\"type\":\"string\",\"enum\":[\"enemy\",\"neutral\",\"friend\",\"ally\"]}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "set_character_value", "Set a player, dialogue NPC, or selected nearby NPC value by ID/name from status, attributes, evaluation, resistance, skill, trait, or feat rows.", "target,field,value",
            "\"target\":{\"type\":\"string\",\"enum\":[\"player\",\"dialogue_npc\",\"nearby_npc\"]},\"field\":{\"type\":\"string\",\"description\":\"Row ID, alias, Chinese/English display name, or element ID.\"},\"value\":{\"type\":\"integer\"},\"category\":{\"type\":\"string\",\"description\":\"Optional: status, attribute, evaluation, resistance, skill, trait, feat, element.\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "set_character_potential", "Set potential for player, dialogue NPC, or selected nearby NPC main attribute/speed.", "target,field,value",
            "\"target\":{\"type\":\"string\",\"enum\":[\"player\",\"dialogue_npc\",\"nearby_npc\"]},\"field\":{\"type\":\"string\",\"description\":\"Attribute/speed row ID or name.\"},\"value\":{\"type\":\"integer\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "set_ability_values", "Set ability or spell level, chance, power, HP/MP/SP cost, and stock for a character. Omitted numbers keep current values.", "target,ability",
            "\"target\":{\"type\":\"string\",\"enum\":[\"player\",\"dialogue_npc\",\"nearby_npc\"]},\"ability\":{\"type\":\"string\",\"description\":\"Ability/spell ID, alias, or name.\"},\"level\":{\"type\":\"integer\"},\"chance\":{\"type\":\"integer\"},\"power\":{\"type\":\"integer\"},\"hp_cost\":{\"type\":\"integer\"},\"mp_cost\":{\"type\":\"integer\"},\"sp_cost\":{\"type\":\"integer\"},\"stock\":{\"type\":\"integer\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "set_npc_relationship", "Set relationship data for dialogue NPC or selected nearby NPC.", "target",
            "\"target\":{\"type\":\"string\",\"enum\":[\"dialogue_npc\",\"nearby_npc\"]},\"affinity\":{\"type\":\"integer\"},\"relationship\":{\"type\":\"string\",\"enum\":[\"enemy\",\"neutral\",\"friend\",\"ally\"]},\"party_action\":{\"type\":\"string\",\"enum\":[\"none\",\"join_party\",\"leave_party\",\"join_faction\",\"leave_faction\"]}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "teleport", "Teleport the player. Landmark and world-position teleport require the player to be on the world map; npc teleport uses selected nearby/dialogue NPC.", "mode",
            "\"mode\":{\"type\":\"string\",\"enum\":[\"to_nearby_npc\",\"to_dialogue_npc\",\"to_landmark\",\"to_world_position\"]},\"landmark\":{\"type\":\"string\",\"description\":\"Landmark name, ID, or UID for to_landmark.\"},\"x\":{\"type\":\"integer\"},\"y\":{\"type\":\"integer\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "set_home_value", "Set selected home basic data, home skill, home feat, or home policy.", "field,value",
            "\"field\":{\"type\":\"string\",\"description\":\"Basic key/name or home element ID/name/alias.\"},\"value\":{\"type\":\"integer\"},\"category\":{\"type\":\"string\",\"description\":\"basic, skill, feat, or policy.\"},\"active\":{\"type\":\"boolean\",\"description\":\"For policy only.\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "set_player_info", "Set player information fields from the Player Info window. Omitted fields are unchanged.", "",
            "\"name\":{\"type\":\"string\"},\"alias\":{\"type\":\"string\"},\"honorific\":{\"type\":\"string\"},\"race_id\":{\"type\":\"string\"},\"job_id\":{\"type\":\"string\"},\"faith_id\":{\"type\":\"string\"},\"faction_id\":{\"type\":\"string\"},\"gender\":{\"type\":\"integer\"},\"age\":{\"type\":\"integer\"},\"height_cm\":{\"type\":\"integer\"},\"weight_kg\":{\"type\":\"integer\"},\"birth_year\":{\"type\":\"integer\"},\"birth_month\":{\"type\":\"integer\"},\"birth_day\":{\"type\":\"integer\"},\"home_word_id\":{\"type\":\"integer\"},\"location_word_id\":{\"type\":\"integer\"},\"father_type_id\":{\"type\":\"integer\"},\"father_prefix_id\":{\"type\":\"integer\"},\"mother_type_id\":{\"type\":\"integer\"},\"mother_prefix_id\":{\"type\":\"integer\"},\"liked_item_id\":{\"type\":\"string\"},\"domain_ids\":{\"type\":\"string\"},\"hobby_ids\":{\"type\":\"string\"},\"work_ids\":{\"type\":\"string\"},\"total_feat_points\":{\"type\":\"integer\"},\"background\":{\"type\":\"string\"},\"memo\":{\"type\":\"string\"},\"memo2\":{\"type\":\"string\"},\"card_note\":{\"type\":\"string\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "set_ui_option", "Set normal UI options except AI Assistant, Debug mode, and Plugin Manager.", "option,value",
            "\"option\":{\"type\":\"string\",\"description\":\"language, ui_style, opacity, font_size, font_color_hex, font_color_follow_style, main_menu_info, elin_modifier_watermark, watermark_position_locked, watermark_game_error_notification, watermark_reset_position, adaptive_ui_scale, custom_ui_scale, force_game_unfocus, ui_rounded_corners, hotkey.\"},\"value\":{\"type\":\"string\",\"description\":\"Value as text, boolean text, number text, or key label. font_size uses 1-28; custom_ui_scale uses -4.0 to 4.0 and only applies when adaptive_ui_scale is off.\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "emp_list_plugins", "List EMP JSON plugins loaded from workspace/Plugin, including function states and operation counts.", "",
            "\"filter\":{\"type\":\"string\",\"description\":\"Optional plugin id, name, relative path, file name, or function text filter.\"},\"limit\":{\"type\":\"integer\",\"description\":\"Maximum plugins to return. Use 0 for all rows. Default 80.\"},\"include_functions\":{\"type\":\"boolean\",\"description\":\"Include per-function rows. Default true.\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "emp_set_function_state", "Set a specific EMP function's enabled/value state and optionally apply it immediately.", "plugin,function",
            "\"plugin\":{\"type\":\"string\",\"description\":\"EMP plugin id, name, relative path, or file name.\"},\"function\":{\"type\":\"string\",\"description\":\"EMP function id or name.\"},\"enabled\":{\"type\":\"boolean\",\"description\":\"Optional enabled state for toggle/patch functions.\"},\"value\":{\"type\":\"string\",\"description\":\"Optional value for value functions or placeholder-driven operations.\"},\"apply\":{\"type\":\"boolean\",\"description\":\"Apply immediately after changing the state. Default true.\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "emp_reload_plugins", "Reload EMP JSON files from workspace/Plugin and re-apply saved states.", "", "");
        sb.Append(",");
        AppendAiToolDefinition(sb, "runtime_reflect_get", "Read a concrete runtime field/property/indexed value. Only use this when the exact readable member path is already known. Do not use it to list an assembly, list a type, inspect source, or target a method. Member paths must use dots, not colon labels.", "target",
            "\"target\":{\"type\":\"string\",\"description\":\"Concrete field/property dot path, for example EClass.pc.idFaith, nearby_npc.faith, EClass.world.date, Type:Namespace.Type.member, Plugin:guid.member, or Assembly:Elin:Namespace.Type.member. Supports dictionary/list/array index reads with square brackets, such as EClass.sources.religions.map[\\\"harmony\\\"].Name or listField[0]. Invalid examples: nearby_npc: faith, Assembly:Elin, Type:GameDate, Assembly:Elin:GameDate.AdvanceMin.\"},\"max_length\":{\"type\":\"integer\",\"description\":\"Maximum returned text length. Default 2000.\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "runtime_list_assemblies", "List loaded runtime assemblies and AI workspace/Decompare decompile caches. Use this to discover valid assembly/plugin names before runtime_search or runtime_list_type. This never reads or writes game state.", "",
            "\"filter\":{\"type\":\"string\",\"description\":\"Optional assembly/plugin/cache name filter, such as Elin, Plugins, BepInEx, or a mod name.\"},\"limit\":{\"type\":\"integer\",\"description\":\"Maximum rows per section. Default 80.\"},\"include_loaded\":{\"type\":\"boolean\",\"description\":\"Include loaded AppDomain assemblies. Default true.\"},\"include_workspace\":{\"type\":\"boolean\",\"description\":\"Include workspace/Decompare *.decompare caches and matching DLL candidates. Default true.\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "runtime_search", "Search plugin workspace/Decompare/<dll>.decompare ILSpy source folders for game and Mod types, fields, properties, and methods. This runs as a background job; if pending, call again later with the same arguments. If an assembly cache is missing, the tool starts workspace/ILSpy/ilspycmd.exe once in the background and returns a retry message instead of scanning live runtime assemblies. Search results include patchable targets when possible.", "query",
            "\"query\":{\"type\":\"string\",\"description\":\"Space/comma separated keywords, such as time date advance hour simulate.\"},\"kind\":{\"type\":\"string\",\"enum\":[\"all\",\"types\",\"members\",\"methods\"],\"description\":\"Default all.\"},\"assembly_filter\":{\"type\":\"string\",\"description\":\"Optional assembly/name filter such as Elin or a plugin name. Recommended for first searches.\"},\"type_filter\":{\"type\":\"string\",\"description\":\"Optional type/fullname filter for narrow live searches or disk filtering.\"},\"live\":{\"type\":\"boolean\",\"description\":\"Optional small real-time reflection search for dynamic cases only. Requires assembly_filter or type_filter. Default false.\"},\"limit\":{\"type\":\"integer\",\"description\":\"Maximum rows to return. Default 80.\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "runtime_list_type", "List fields, properties, methods, and source snippets of a type from plugin workspace/Decompare/<dll>.decompare ILSpy source folders. This runs as a background job; if pending, call again later with the same arguments. Use after runtime_search to inspect exact member and method names and patchable targets.", "type",
            "\"type\":{\"type\":\"string\",\"description\":\"Type name or full name, optionally with Assembly: prefix.\"},\"member_filter\":{\"type\":\"string\",\"description\":\"Optional member name/type filter.\"},\"include_methods\":{\"type\":\"boolean\",\"description\":\"Default true.\"},\"include_fields\":{\"type\":\"boolean\",\"description\":\"Default true.\"},\"include_properties\":{\"type\":\"boolean\",\"description\":\"Default true.\"},\"limit\":{\"type\":\"integer\",\"description\":\"Maximum rows to return. Default 160.\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "runtime_reflect_set", "High-risk runtime reflection: write a field/property in the game or another loaded Mod/plugin. This creates a pending action and only executes after the user confirms in the UI.", "target,value",
            "\"target\":{\"type\":\"string\",\"description\":\"Expression like EClass.pc.hp, Type:Namespace.Type.member, Plugin:guid.member, or Assembly:Type.member.\"},\"value\":{\"type\":\"string\",\"description\":\"Value text parsed into the target member type.\"},\"value_type\":{\"type\":\"string\",\"description\":\"Optional explicit value type such as string, int, float, bool, enum, null.\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "runtime_invoke_method", "High-risk runtime reflection: invoke a concrete method in the game or another loaded Mod/plugin. This creates a pending action and only executes after the user confirms in the UI. Use after runtime_search/runtime_list_type finds a method that directly performs the requested operation.", "target",
            "\"target\":{\"type\":\"string\",\"description\":\"Concrete method target, for example Assembly:Elin:GameDate.AdvanceHour, Type:SomeType.StaticMethod, EClass.pc.SomeInstanceMethod, or Plugin:guid:MethodName. For instance methods found on a type, pass instance, for example target=Assembly:Elin:Chara.SetFaith and instance=EClass.pc.\"},\"instance\":{\"type\":\"string\",\"description\":\"Optional instance expression for invoking a non-static method found on a type, such as EClass.pc, dialogue_npc, nearby_npc, or Plugin:guid.\"},\"args\":{\"type\":\"string\",\"description\":\"Optional JSON-like string array or comma/newline separated argument values. Values are parsed into the target method parameter types. Use ref:<expression> to pass an existing runtime object, for example ref:EClass.pc.\"},\"max_length\":{\"type\":\"integer\",\"description\":\"Maximum returned result text length. Default 2000.\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "runtime_harmony_patch", "High-risk runtime Harmony patch for a loaded game or other Mod method. This creates a pending action and only executes after the user confirms in the UI. Supports modes: suppress_exceptions, skip_original, force_return, prefix, postfix, prefix_code, postfix_code. Target must be a concrete method, never an assembly name alone.", "target,mode",
            "\"target\":{\"type\":\"string\",\"description\":\"Concrete method target from runtime_search/runtime_list_type. Examples: Assembly:Elin:GameDate.AdvanceMin, Assembly:Elin:VirtualDate.SimulateHour, Type:GameDate.AdvanceMin. Do not use Elin alone.\"},\"mode\":{\"type\":\"string\",\"enum\":[\"suppress_exceptions\",\"skip_original\",\"force_return\",\"prefix\",\"postfix\",\"prefix_code\",\"postfix_code\"],\"description\":\"suppress_exceptions catches exceptions; skip_original prevents original execution; force_return sets __result and skips original; prefix/postfix install custom logic from patch_method or code. patch_method and code are equal-priority sources.\"},\"return_value\":{\"type\":\"string\",\"description\":\"Required for force_return unless method returns void.\"},\"patch_method\":{\"type\":\"string\",\"description\":\"Equal-priority patch logic source: existing static patch method target, for example Assembly:MyPatchDll:MyPatchClass.Prefix or Type:MyPatchClass.Postfix. Used by prefix/postfix modes.\"},\"code\":{\"type\":\"string\",\"description\":\"Equal-priority patch logic source: C# patch code for prefix/postfix modes. May be a method body or a full static method/class. Use directly when generated code is the most direct way to satisfy the request.\"},\"patch_id\":{\"type\":\"string\",\"description\":\"Optional ID used later by runtime_harmony_unpatch. If it already exists, the modifier auto-adds a numeric suffix.\"}");
        sb.Append(",");
        AppendAiToolDefinition(sb, "runtime_harmony_unpatch", "High-risk runtime Harmony unpatch for a patch created by runtime_harmony_patch. This creates a pending action and only executes after the user confirms in the UI.", "patch_id",
            "\"patch_id\":{\"type\":\"string\",\"description\":\"Patch ID returned by runtime_harmony_patch, or all to remove all AI runtime patches.\"}");
        sb.Append("]");
    }
    private static void AppendAiToolDefinition(StringBuilder sb, string name, string description, string requiredCsv, string propertiesJson)
    {
        sb.Append("{\"type\":\"function\",\"function\":{\"name\":\"")
            .Append(EscapeJson(name))
            .Append("\",\"description\":\"")
            .Append(EscapeJson(description))
            .Append("\",\"parameters\":{\"type\":\"object\",\"properties\":{")
            .Append(propertiesJson)
            .Append("}");
        if (!string.IsNullOrWhiteSpace(requiredCsv))
        {
            sb.Append(",\"required\":[");
            var parts = requiredCsv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("\"").Append(EscapeJson(parts[i].Trim())).Append("\"");
            }
            sb.Append("]");
        }
        sb.Append("}}}");
    }
    private static string NormalizeAiChatRole(string role)
    {
        if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase))
            return "assistant";
        if (string.Equals(role, "system", StringComparison.OrdinalIgnoreCase))
            return "system";
        return "user";
    }
    private static List<string> ParseAiModelIds(string json)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(json))
            return result;

        var matches = Regex.Matches(json, "\"id\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in matches)
        {
            if (!match.Success || match.Groups.Count < 2)
                continue;
            var id = UnescapeJsonString(match.Groups[1].Value);
            if (string.IsNullOrWhiteSpace(id))
                continue;
            if (seen.Add(id))
                result.Add(id);
        }
        return result;
    }
    private static string ExtractAiChatContent(string json)
    {
        if (string.IsNullOrEmpty(json))
            return "";

        var match = Regex.Match(json, "\"content\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"");
        if (!match.Success || match.Groups.Count < 2)
            return "";
        return UnescapeJsonString(match.Groups[1].Value);
    }
    private static string? ExtractAiStreamData(string line)
    {
        if (line == null)
            return null;
        line = line.Trim();
        if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return null;
        return line.Substring(5).Trim();
    }
    private static string ExtractAiStreamDelta(string json)
    {
        if (string.IsNullOrEmpty(json))
            return "";

        var match = Regex.Match(json, "\"content\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"");
        if (!match.Success || match.Groups.Count < 2)
            return "";
        return UnescapeJsonString(match.Groups[1].Value);
    }
    private static List<AiToolCall> ExtractAiToolCalls(string json)
    {
        var result = new List<AiToolCall>();
        if (string.IsNullOrEmpty(json))
            return result;

        var array = ExtractJsonArrayProperty(json, "tool_calls");
        if (string.IsNullOrEmpty(array))
            return result;

        foreach (var item in EnumerateTopLevelJsonObjects(array))
        {
            var function = ExtractJsonObjectProperty(item, "function");
            var name = JsonStringProperty(function, "name");
            var arguments = JsonStringProperty(function, "arguments");
            if (string.IsNullOrEmpty(name))
                continue;
            var id = JsonStringProperty(item, "id");
            result.Add(new AiToolCall(id, name, arguments));
        }
        return result;
    }
    private static string BuildAiToolFollowupPrompt(string originalPrompt, string toolResults)
    {
        return "The user asked:\n" + originalPrompt + "\n\n" +
               "EMG (Elin Modifier Gateway) has executed with these results:\n" + toolResults + "\n\n" +
               "Briefly summarize what was done, what failed, and any important limitation. Only state that data was read, verified, changed, spawned, patched, or completed when an EMG result explicitly says ok or pending_confirmation for that action. If EMG results are failed or found no results, explicitly say the read/change did not succeed and do not infer values from guesses. Check the summary against every explicit part of the user's request so no requested target, constraint, exclusion, or verification need is silently ignored. If the task failed or is incomplete, state the most concrete next EMG/search/target to try rather than saying it is impossible. Do not call EMG tools.";
    }
    private static string BuildAiToolContinuePrompt(string originalPrompt, string toolResults)
    {
        return "The user asked:\n" + originalPrompt + "\n\n" +
               "EMG (Elin Modifier Gateway) has executed these results so far:\n" + toolResults + "\n\n" +
               "Continue completing the user's request. First re-check the request as a set of explicit requirements, constraints, targets, exclusions, and verification needs, then continue until each related part is handled or clearly blocked. If these results only identified a target, call the next appropriate tool to actually perform the requested operation. " +
               "Do not stop after lookup-only EMG tools such as list_inventory_items or list_game_names when the user requested a modification, spawn, teleport, or other action. " +
               "If a request refers to a broader game system, search all meaningfully related names, IDs, display names, translated terms, nearby data tables, source definitions, owner modules, callers, and callees; broaden or narrow runtime_search/runtime_list_type queries as needed instead of relying on one literal query. " +
               "For player faith/religion changes, if list_game_names returns a real religion ID, call set_player_info with faith_id set to that ID; do not conclude that changing faith is impossible merely because the first lookup found an NPC/god entry. If no real religion ID exists, explain that only existing religion IDs are supported by normal EMG tools. " +
               "If the user requested deleting or clearing inventory and no pending_confirmation was produced yet, call delete_inventory_items with the appropriate scope. Do not ask for confirmation in plain text. " +
               "After any state-changing built-in EMG call, verify the new value with the matching read/list EMG tool when available; if verification does not match, continue with a more specific target such as UID or exact ID. " +
               "For runtime reflection, invocation, or patching requests, do not call runtime_reflect_get on assembly-only/type-only/method targets. Use dot paths for object members, such as nearby_npc.faith or EClass.pc.idFaith; never use colon labels like nearby_npc: faith. For dictionary/list/array reads, use square brackets such as EClass.sources.religions.map[\"harmony\"].Name or listField[0]. If an assembly name is unknown, call runtime_list_assemblies. If guessed fields/properties fail, call disk-backed runtime_search and runtime_list_type to discover real types/members/methods, wait for pending background search results by calling the same tool again after a moment, then queue runtime_reflect_set, runtime_invoke_method, or runtime_harmony_patch if a plausible target exists. Prefer invoking an existing game method when it directly performs the requested operation; prefer patching an update/advance/check method when behavior must persist. For custom Prefix/Postfix code, runtime_harmony_patch with mode=prefix_code or mode=postfix_code and code=<C# code> is the correct and only EMG patch-code tool. Do not mention runtime_list_code, runtime_patch, code_inject, PatchGenCns, or PatchGen. If runtime_invoke_method reports that an instance method requires an instance target, retry with instance=EClass.pc, instance=dialogue_npc, instance=nearby_npc, or another concrete object expression instead of giving up. If a target is rejected, ambiguous, or too shallow to plausibly affect the requested behavior, search caller/callee methods and use the most central update/entry method. " +
               "If runtime_reflect_get fails with unknown root, type not found, or member not found, do not summarize it as a successful read; correct the path, try known roots like EClass.pc/dialogue_npc/nearby_npc, or inspect the type with runtime_list_type before concluding. " +
               "If a runtime_search/runtime_list_type result is failed because it is still pending after retries, do not treat it as no result; retry later or use narrower/different keywords, assembly_filter, and type_filter. If it says found no results, report no results for that exact query and try different keywords before concluding failure. " +
               "For freezing or stopping game time, prefer searching for time/date advance/simulate methods and queueing a skip_original Harmony patch over guessing boolean fields; if minute-level patches do not cover hour/day updates, search and patch the higher-level advance/simulate method. " +
               "If the request is already fully completed or cannot be completed, answer without calling tools.";
    }
}
