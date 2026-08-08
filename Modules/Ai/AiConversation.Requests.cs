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
    private bool TryHandleAiDangerousActionConfirmation(string prompt)
    {
        var key = NormalizeAiKey(prompt);
        var confirm = key == "确认" || key == "確定" || key == "执行" || key == "執行" ||
                      key == "confirm" || key == "yes" || key == "y" || key == "ok" ||
                      key == "run" || key == "execute" || key == "apply";
        var cancel = key == "取消" || key == "撤销" || key == "不执行" ||
                     key == "cancel" || key == "no" || key == "n" || key == "abort" || key == "discard";
        if (!confirm && !cancel)
            return false;

        var userText = _aiPrompt;
        _aiPrompt = "";
        AppendAiTranscriptBlock("User", "User", userText);
        AppendAiTranscriptHeader("AI", "AI");

        if (_aiPendingDangerousActions.Count == 0)
        {
            var text = T(
                "没有待确认的高危操作。请先让 AI 生成需要确认的操作，再回复“确认”。",
                "There is no pending high-risk action. Ask the AI to queue an action first, then reply \"confirm\".");
            _aiResponse += text;
            _aiLog = text;
            ScrollAiResponseToBottom();
            return true;
        }

        if (cancel)
        {
            var count = _aiPendingDangerousActions.Count;
            _aiPendingDangerousActions.Clear();
            var text = T("已取消待执行的高危操作: ", "Cancelled pending high-risk actions: ") + count.ToString(CultureInfo.InvariantCulture);
            _aiResponse += text;
            _aiLog = text;
            ScrollAiResponseToBottom();
            return true;
        }

        var sb = new StringBuilder();
        var actions = _aiPendingDangerousActions.ToArray();
        _aiPendingDangerousActions.Clear();
        for (var i = 0; i < actions.Length; i++)
        {
            var action = actions[i];
            try
            {
                var result = ExecuteAiDangerousActionNow(action);
                sb.Append("#").Append(action.Id.ToString(CultureInfo.InvariantCulture)).Append(" ")
                    .Append(action.ToolName).Append(": ").AppendLine(result);
            }
            catch (Exception ex)
            {
                sb.Append("#").Append(action.Id.ToString(CultureInfo.InvariantCulture)).Append(" ")
                    .Append(action.ToolName).Append(": failed: ").AppendLine(ex.Message);
            }
        }
        var resultText = T("已执行确认的高危操作:", "Confirmed high-risk actions executed:") + "\n" + sb.ToString().TrimEnd();
        _aiResponse += resultText;
        _aiLog = T("高危操作执行完成", "High-risk actions executed");
        ScrollAiResponseToBottom();
        return true;
    }
    private List<string> GetFilteredAiModels()
    {
        var result = new List<string>();
        var filter = _aiModelFilter ?? "";
        foreach (var model in _aiModels)
        {
            if (string.IsNullOrEmpty(filter) || model.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                result.Add(model);
        }
        return result;
    }
    private void FetchAiModels()
    {
        var apiBase = NormalizeAiApiBase(_aiApiBase);
        ApplyAiHttpTimeoutSecondsText(false);
        if (string.IsNullOrWhiteSpace(apiBase))
        {
            _aiLog = T("API地址不能为空", "API base cannot be empty");
            return;
        }
        if (string.IsNullOrWhiteSpace(_aiApiKey))
        {
            _aiLog = T("APIKEY不能为空", "API key cannot be empty");
            return;
        }

        _aiFetchModelsInProgress = true;
        _aiLog = T("正在获取模型列表...", "Fetching model list...");
        var apiKey = _aiApiKey;
        _aiLastRequestBody = "";
        RunAiTask(
            () => AiGetAsync(BuildAiEndpoint(apiBase, "models"), apiKey, _aiHttpTimeoutSeconds, CancellationToken.None),
            json =>
            {
                _aiLastResponseBody = json;
                var models = ParseAiModelIds(json);
                _aiModels.Clear();
                _aiModels.AddRange(models);
                _aiModels.Sort(StringComparer.OrdinalIgnoreCase);
                _aiLog = models.Count == 0 ? T("未获取到模型名", "No models found") : T("已获取模型数量: ", "Models fetched: ") + models.Count.ToString(CultureInfo.InvariantCulture);
                if (IsLGuiInitialized() && _lGuiPage == LGuiPage.Ai)
                    SwitchLGuiPage(LGuiPage.Ai);
            },
            ex => _aiLog = T("获取模型列表失败: ", "Failed to fetch models: ") + ex.Message,
            () => _aiFetchModelsInProgress = false);
    }
    private void SendAiChat()
    {
        var apiBase = NormalizeAiApiBase(_aiApiBase);
        ApplyAiHttpTimeoutSecondsText(false);
        if (string.IsNullOrWhiteSpace(apiBase))
        {
            _aiLog = T("API地址不能为空", "API base cannot be empty");
            return;
        }
        if (string.IsNullOrWhiteSpace(_aiApiKey))
        {
            _aiLog = T("APIKEY不能为空", "API key cannot be empty");
            return;
        }
        if (string.IsNullOrWhiteSpace(_aiModelName))
        {
            _aiLog = T("模型名不能为空", "Model cannot be empty");
            return;
        }
        if (string.IsNullOrWhiteSpace(_aiPrompt))
        {
            _aiLog = T("输入内容不能为空", "Prompt cannot be empty");
            return;
        }
        if (TryHandleAiDangerousActionConfirmation(_aiPrompt))
            return;

        var apiKey = _aiApiKey;
        var prompt = _aiPrompt;
        var reasoningEffort = GetAiReasoningEffort();
        BeginAiOperation();
        _aiCurrentPrompt = prompt;
        _aiCurrentToolResults = "";
        _aiCurrentPartialResponse = "";
        _aiPrompt = "";
        AppendAiTranscriptBlock("User", "User", prompt);
        AppendAiTranscriptHeader("AI", "AI");
        ScrollAiResponseToBottom();

        if (ShouldAutoCompressAiContextBeforeSend(prompt))
        {
            _aiSendInProgress = true;
            StartAiContextCompression(apiBase, apiKey, reasoningEffort, false, () =>
            {
                StartAiChatAfterCompression(apiBase, apiKey, prompt, reasoningEffort);
            }, prompt);
            return;
        }

        StartAiChatAfterCompression(apiBase, apiKey, prompt, reasoningEffort);
    }
    private void BeginAiOperation()
    {
        CancelAiCancellationOnly();
        _aiRunId++;
        _aiCancellation = new CancellationTokenSource();
    }
    private void CancelAiCurrentOperation()
    {
        if (!_aiSendInProgress && !_aiCompressionInProgress)
            return;
        var reason = T("用户中止本次对话", "User aborted this chat turn");
        CancelAiCancellationOnly();
        _aiRunId++;
        _aiSendInProgress = false;
        _aiCompressionInProgress = false;
        if (!string.IsNullOrEmpty(_aiResponse) && !_aiResponse.EndsWith("\n", StringComparison.Ordinal))
            _aiResponse += "\n";
        _aiResponse += reason;
        _aiLog = reason;
        RecordAiInterruptedContext(_aiCurrentPrompt, "user_abort", _aiCurrentToolResults, _aiCurrentPartialResponse, reason);
        ScrollAiResponseToBottom();
    }
    private void CancelAiCancellationOnly()
    {
        try
        {
            if (_aiCancellation != null)
                _aiCancellation.Cancel();
        }
        catch { }
        try
        {
            if (_aiCancellation != null)
                _aiCancellation.Dispose();
        }
        catch { }
        _aiCancellation = null;
    }
    private CancellationToken GetAiCancellationToken()
    {
        return _aiCancellation == null ? CancellationToken.None : _aiCancellation.Token;
    }
    private bool IsCurrentAiRun(int runId)
    {
        return runId == _aiRunId && (_aiCancellation == null || !_aiCancellation.IsCancellationRequested);
    }
    private void ScrollAiResponseToBottom()
    {
    }
    private void StartAiChatAfterCompression(string apiBase, string apiKey, string prompt, string reasoningEffort)
    {
        _aiSendInProgress = true;
        _aiLog = T("正在发送...", "Sending...");
        var body = BuildAiChatJson(_aiModelName, prompt, _aiMessages, _aiUseContext, IsAiReasoningEnabled(), reasoningEffort, true, false, _aiUseToolStreaming);
        _aiLastRequestBody = body;
        RunAiToolLoop(apiBase, apiKey, prompt, body, reasoningEffort, "", 0);
    }
    private void StartManualAiContextCompression()
    {
        var apiBase = NormalizeAiApiBase(_aiApiBase);
        if (string.IsNullOrWhiteSpace(apiBase))
        {
            _aiLog = T("API地址不能为空", "API base cannot be empty");
            return;
        }
        if (string.IsNullOrWhiteSpace(_aiApiKey))
        {
            _aiLog = T("APIKEY不能为空", "API key cannot be empty");
            return;
        }
        if (string.IsNullOrWhiteSpace(_aiModelName))
        {
            _aiLog = T("模型名不能为空", "Model cannot be empty");
            return;
        }
        if (_aiMessages.Count == 0)
        {
            _aiLog = T("没有可压缩的上下文", "No context to compact");
            return;
        }

        StartAiContextCompression(apiBase, _aiApiKey, GetAiReasoningEffort(), true, null);
    }
    private bool ShouldAutoCompressAiContextBeforeSend(string nextPrompt)
    {
        if (!_aiUseContext || !_aiAutoCompressContext || _aiMessages.Count == 0)
            return false;
        if (EstimateAiCompressibleContextLength(_aiMessages) <= 0)
            return false;
        return EstimateAiContextLength(_aiMessages) + (nextPrompt == null ? 0 : nextPrompt.Length) >= _aiContextCompressThreshold;
    }
    private void StartAiContextCompression(string apiBase, string apiKey, string reasoningEffort, bool manual, Action onSuccess, string interruptedPrompt = null)
    {
        var runId = _aiRunId;
        if (_aiCompressionInProgress)
            return;
        if (_aiMessages.Count == 0)
        {
            if (manual)
                _aiLog = T("没有可压缩的上下文", "No context to compact");
            if (onSuccess != null)
                onSuccess();
            return;
        }

        var transcript = BuildAiContextCompressionTranscript(_aiMessages, false);
        if (string.IsNullOrWhiteSpace(transcript))
        {
            if (manual)
                _aiLog = T("没有可压缩的 AI/EMG 上下文", "No AI/EMG context to compact");
            if (onSuccess != null)
                onSuccess();
            return;
        }

        _aiCompressionInProgress = true;
        _aiLog = manual ? T("正在压缩上下文...", "Compacting context...") : T("上下文过长，正在自动压缩...", "Context is long, auto compacting...");
        var prompt = BuildAiContextCompressionPrompt(transcript);
        var body = BuildAiChatJson(_aiModelName, prompt, new List<AiChatMessage>(), false, IsAiReasoningEnabled(), reasoningEffort, false, false, false);
        _aiLastRequestBody = body;
        RunAiTask(
            () => AiPostAsync(BuildAiEndpoint(apiBase, "chat/completions"), apiKey, body, _aiHttpTimeoutSeconds, GetAiCancellationToken()),
            json =>
            {
                if (!IsCurrentAiRun(runId))
                    return;
                _aiLastResponseBody = json;
                var summary = ExtractAiChatContent(json);
                if (string.IsNullOrWhiteSpace(summary))
                    summary = TruncateForLog(transcript, Math.Max(2000, _aiContextCompressThreshold / 2));
                ApplyAiContextSummary(summary, _aiMessages);
                _aiLog = T("上下文已压缩", "Context compacted") + " " + GetAiContextUsageLabel();
                if (manual)
                {
                    AppendAiTranscriptHeader("AI", "AI");
                    _aiResponse += _aiLog;
                    ScrollAiResponseToBottom();
                }
                if (onSuccess != null)
                    onSuccess();
            },
            ex =>
            {
                if (!IsCurrentAiRun(runId))
                    return;
                var errorText = T("上下文压缩失败: ", "Context compaction failed: ") + ex.Message;
                _aiLog = errorText;
                if (manual)
                {
                    AppendAiTranscriptHeader("AI", "AI");
                    _aiResponse += errorText;
                    ScrollAiResponseToBottom();
                }
                else
                {
                    _aiResponse += errorText;
                    RecordAiInterruptedContext(interruptedPrompt, "context_compaction", "", "", errorText);
                    _aiSendInProgress = false;
                }
            },
            () => { if (IsCurrentAiRun(runId)) _aiCompressionInProgress = false; });
    }
    private void ApplyAiContextSummary(string summary, IEnumerable<AiChatMessage> originalMessages)
    {
        summary = (summary ?? "").Trim();
        if (summary.Length == 0)
            return;
        var preservedUserMessages = new List<AiChatMessage>();
        if (originalMessages != null)
        {
            foreach (var message in originalMessages)
            {
                if (message == null || string.IsNullOrEmpty(message.Content))
                    continue;
                if (string.Equals(NormalizeAiChatRole(message.Role), "user", StringComparison.OrdinalIgnoreCase))
                    preservedUserMessages.Add(new AiChatMessage("user", message.Content));
            }
        }

        _aiMessages.Clear();
        _aiMessages.Add(new AiChatMessage("system", "Compressed AI/EMG context. User messages below are preserved verbatim and remain authoritative:\n" + summary));
        for (var i = 0; i < preservedUserMessages.Count; i++)
            _aiMessages.Add(preservedUserMessages[i]);
    }
    private static string BuildAiContextCompressionPrompt(string transcript)
    {
        return "Compress only the following Elin Modifier assistant/tool context into a compact but complete memory for future turns. " +
               "The user's original messages are intentionally excluded from this transcript and will be preserved verbatim outside the summary, so do not invent or rewrite user wording. " +
               "Preserve important decisions, current game/mod state, EMG results, pending risks, exact names/IDs/UIDs, patch IDs, configuration values, and unresolved tasks. " +
               "Preserve multi-part requirements as checklist-like facts only when they are visible in assistant/tool context, including constraints, exclusions, verification requests, and parts that were not finished yet. " +
               "Preserve failed or incomplete tool attempts as diagnostic state, including what was tried, what error occurred, and what next target/search/action should be attempted. " +
               "Remove repetition and UI chatter. Do not invent facts. Return only the compressed context.\n\n" +
               transcript;
    }
    private static string BuildAiContextCompressionTranscript(IEnumerable<AiChatMessage> messages, bool includeUserMessages)
    {
        var sb = new StringBuilder();
        if (messages != null)
        {
            foreach (var message in messages)
            {
                if (message == null || string.IsNullOrEmpty(message.Content))
                    continue;
                var role = NormalizeAiChatRole(message.Role);
                if (!includeUserMessages && string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
                    continue;
                sb.Append(role).Append(": ").AppendLine(message.Content);
                sb.AppendLine();
            }
        }
        return sb.ToString().TrimEnd();
    }
    private int EstimateAiContextLength(IEnumerable<AiChatMessage> messages)
    {
        var length = 0;
        if (messages != null)
        {
            foreach (var message in messages)
            {
                if (message == null)
                    continue;
                length += (message.Role == null ? 0 : message.Role.Length) + 2;
                length += message.Content == null ? 0 : message.Content.Length;
            }
        }
        return length;
    }
    private int EstimateAiCompressibleContextLength(IEnumerable<AiChatMessage> messages)
    {
        var length = 0;
        if (messages != null)
        {
            foreach (var message in messages)
            {
                if (message == null || string.IsNullOrEmpty(message.Content))
                    continue;
                if (string.Equals(NormalizeAiChatRole(message.Role), "user", StringComparison.OrdinalIgnoreCase))
                    continue;
                length += (message.Role == null ? 0 : message.Role.Length) + 2;
                length += message.Content.Length;
            }
        }
        return length;
    }
    private string GetAiContextUsageLabel()
    {
        return EstimateAiContextLength(_aiMessages).ToString(CultureInfo.InvariantCulture) + "/" +
               _aiContextCompressThreshold.ToString(CultureInfo.InvariantCulture);
    }
    private void ApplyAiContextCompressionThresholdText()
    {
        _aiContextCompressThreshold = Clamp(ParseInt(_aiContextCompressThresholdText, AiContextCompressionDefaultThreshold), AiContextCompressionMinThreshold, AiContextCompressionMaxThreshold);
        _aiContextCompressThresholdText = _aiContextCompressThreshold.ToString(CultureInfo.InvariantCulture);
        _aiLog = T("上下文压缩阈值已更新", "Context compaction threshold updated");
    }
    private void ApplyAiHttpTimeoutSecondsText(bool updateLog = true)
    {
        _aiHttpTimeoutSeconds = Clamp(ParseInt(_aiHttpTimeoutSecondsText, AiHttpTimeoutDefaultSeconds), AiHttpTimeoutMinSeconds, AiHttpTimeoutMaxSeconds);
        _aiHttpTimeoutSecondsText = _aiHttpTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        if (updateLog)
            _aiLog = T("HTTP超时时间已更新", "HTTP timeout updated");
    }
    private void RunAiToolLoop(string apiBase, string apiKey, string originalPrompt, string body, string reasoningEffort, string accumulatedToolResults, int round)
    {
        var runId = _aiRunId;
        RunAiTask(
            () => AiPostAsync(BuildAiEndpoint(apiBase, "chat/completions"), apiKey, body, _aiHttpTimeoutSeconds, GetAiCancellationToken()),
            json =>
            {
                if (!IsCurrentAiRun(runId))
                    return;
                _aiLastResponseBody = json;
                var toolCalls = ExtractAiToolCalls(json);
                if (toolCalls.Count == 0)
                {
                    var responseText = ExtractAiChatContent(json);
                    _aiResponse += responseText;
                    ScrollAiResponseToBottom();
                    _aiMessages.Add(new AiChatMessage("user", originalPrompt));
                    _aiMessages.Add(new AiChatMessage("assistant", (string.IsNullOrEmpty(accumulatedToolResults) ? "" : accumulatedToolResults + "\n\n") + responseText));
                    _aiLog = T("请求完成", "Request completed");
                    _aiSendInProgress = false;
                    return;
                }

                var toolResultText = ExecuteAiToolCalls(toolCalls);
                var retryToolCalls = GetAiRuntimeWorkspaceRetryToolCalls(toolCalls);
                if (retryToolCalls.Count > 0 && IsAiRuntimeWorkspacePendingToolResult(toolResultText))
                {
                    _aiLog = T("后台检索中...", "Searching in background...");
                    StartCoroutine(RetryAiRuntimeWorkspacePendingToolCalls(apiBase, apiKey, originalPrompt, reasoningEffort, CombineAiToolResults(accumulatedToolResults, toolResultText), round, retryToolCalls, 0, runId));
                    return;
                }
                ContinueAiToolLoopAfterToolResults(apiBase, apiKey, originalPrompt, reasoningEffort, accumulatedToolResults, round, toolResultText);
            },
            ex =>
            {
                if (!IsCurrentAiRun(runId))
                    return;
                var errorText = T("请求失败: ", "Request failed: ") + ex.Message;
                _aiResponse += errorText;
                _aiLog = errorText;
                RecordAiInterruptedContext(originalPrompt, "tool_loop_round_" + round.ToString(CultureInfo.InvariantCulture), accumulatedToolResults, "", errorText);
                _aiSendInProgress = false;
            },
            () => { });
    }
    private IEnumerator RetryAiRuntimeWorkspacePendingToolCalls(string apiBase, string apiKey, string originalPrompt, string reasoningEffort, string accumulatedToolResults, int round, List<AiToolCall> toolCalls, int retry, int runId)
    {
        var delay = retry < 24 ? 1f : 2f;
        yield return new WaitForSecondsRealtime(delay);
        if (!IsCurrentAiRun(runId))
            yield break;

        var toolResultText = ExecuteAiToolCalls(toolCalls);
        var combinedSoFar = CombineAiToolResults(accumulatedToolResults, toolResultText);
        var retryToolCalls = GetAiRuntimeWorkspaceRetryToolCalls(toolCalls);
        if (retryToolCalls.Count > 0 &&
            IsAiRuntimeWorkspacePendingToolResult(toolResultText) &&
            retry + 1 < AiRuntimeWorkspacePendingRetryMax)
        {
            _aiLog = T("后台检索中...", "Searching in background...") + " " + (retry + 2).ToString(CultureInfo.InvariantCulture) + "/" + AiRuntimeWorkspacePendingRetryMax.ToString(CultureInfo.InvariantCulture);
            StartCoroutine(RetryAiRuntimeWorkspacePendingToolCalls(apiBase, apiKey, originalPrompt, reasoningEffort, combinedSoFar, round, retryToolCalls, retry + 1, runId));
            yield break;
        }

        ContinueAiToolLoopAfterToolResults(apiBase, apiKey, originalPrompt, reasoningEffort, accumulatedToolResults, round, BuildAiRuntimeWorkspaceRetryFinalResult(toolResultText, retry + 1 >= AiRuntimeWorkspacePendingRetryMax));
    }
    private void ContinueAiToolLoopAfterToolResults(string apiBase, string apiKey, string originalPrompt, string reasoningEffort, string accumulatedToolResults, int round, string toolResultText)
    {
        var combinedToolResults = CombineAiToolResults(accumulatedToolResults, toolResultText);
        _aiCurrentToolResults = combinedToolResults;
        if (!string.IsNullOrEmpty(toolResultText))
        {
            AppendAiResponseBlock(T("EMG执行结果:", "EMG execution results:"), toolResultText);
        }

        if (HasOnlyPendingWorkspaceResults(combinedToolResults))
        {
            var exhausted = "failed: runtime workspace search is still pending after retries. No completed result was returned yet. Try narrower keywords, assembly_filter, or type_filter; do not treat this as proof that no result exists.";
            ContinueAiToolLoopAfterToolResults(apiBase, apiKey, originalPrompt, reasoningEffort, combinedToolResults, round, exhausted);
            return;
        }

        if (round >= 8)
        {
            StartAiFinalResponse(apiBase, apiKey, originalPrompt, combinedToolResults, reasoningEffort);
            return;
        }

        var continuePrompt = BuildAiToolContinuePrompt(originalPrompt, combinedToolResults);
        var continueBody = BuildAiChatJson(_aiModelName, continuePrompt, _aiMessages, false, IsAiReasoningEnabled(), reasoningEffort, true, false, _aiUseToolStreaming);
        _aiLastRequestBody = continueBody;
        _aiLog = T("继续执行EMG...", "Continuing EMG actions...");
        RunAiToolLoop(apiBase, apiKey, originalPrompt, continueBody, reasoningEffort, combinedToolResults, round + 1);
    }
    private static string CombineAiToolResults(string existing, string next)
    {
        if (string.IsNullOrEmpty(existing))
            return next ?? "";
        if (string.IsNullOrEmpty(next))
            return existing;
        return existing + "\n" + next;
    }
    private void AppendAiResponseBlock(string title, string body)
    {
        title = (title ?? "").TrimEnd();
        body = (body ?? "").Trim();
        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(body))
            return;
        _aiResponse = (_aiResponse ?? "").TrimEnd();
        if (_aiResponse.Length > 0)
            _aiResponse += "\n\n";
        if (!string.IsNullOrEmpty(title))
            _aiResponse += title + "\n";
        if (!string.IsNullOrEmpty(body))
            _aiResponse += body;
        ScrollAiResponseToBottom();
    }
    private static List<AiToolCall> GetAiRuntimeWorkspaceRetryToolCalls(List<AiToolCall> toolCalls)
    {
        var result = new List<AiToolCall>();
        if (toolCalls == null)
            return result;
        for (var i = 0; i < toolCalls.Count; i++)
        {
            var name = NormalizeAiKey(toolCalls[i].Name);
            if (name == "runtime_search" || name == "runtime_list_type")
                result.Add(toolCalls[i]);
        }
        return result;
    }
    private static bool IsAiRuntimeWorkspacePendingToolResult(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        return text.IndexOf("pending: runtime_search", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("pending: runtime_list_type", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("pending: ILSpy", StringComparison.OrdinalIgnoreCase) >= 0;
    }
    private static string BuildAiRuntimeWorkspaceRetryFinalResult(string text, bool retryExhausted)
    {
        if (string.IsNullOrWhiteSpace(text))
            return retryExhausted
                ? "failed: runtime workspace search returned no output before retry limit."
                : "";
        if (!IsAiRuntimeWorkspacePendingToolResult(text))
            return text;
        if (!retryExhausted)
            return text;
        return text + "\nfailed: runtime workspace search is still pending after retry limit. This is a timeout/pending state, not a no-result state. Retry later or narrow query/assembly_filter/type_filter.";
    }
    private static bool HasOnlyPendingWorkspaceResults(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (!IsAiRuntimeWorkspacePendingToolResult(text))
            return false;
        return text.IndexOf("ok: runtime search results", StringComparison.OrdinalIgnoreCase) < 0 &&
               text.IndexOf("ok: runtime type", StringComparison.OrdinalIgnoreCase) < 0 &&
               text.IndexOf("failed:", StringComparison.OrdinalIgnoreCase) < 0 &&
               text.IndexOf("pending_confirmation:", StringComparison.OrdinalIgnoreCase) < 0 &&
               text.IndexOf("ok: invoked", StringComparison.OrdinalIgnoreCase) < 0 &&
               text.IndexOf("ok: runtime Harmony patch", StringComparison.OrdinalIgnoreCase) < 0 &&
               text.IndexOf("ok: ", StringComparison.OrdinalIgnoreCase) < 0;
    }
    private void StartAiFinalResponse(string apiBase, string apiKey, string originalPrompt, string toolResults, string reasoningEffort)
    {
        var runId = _aiRunId;
        var followupPrompt = BuildAiToolFollowupPrompt(originalPrompt, toolResults);
        var followupBody = BuildAiChatJson(_aiModelName, followupPrompt, _aiMessages, false, IsAiReasoningEnabled(), reasoningEffort, false, _aiUseStreaming, false);
        _aiLastRequestBody = followupBody;
        if (!_aiUseStreaming)
        {
            RunAiTask(
                () => AiPostAsync(BuildAiEndpoint(apiBase, "chat/completions"), apiKey, followupBody, _aiHttpTimeoutSeconds, GetAiCancellationToken()),
                json =>
                {
                    if (!IsCurrentAiRun(runId))
                        return;
                    _aiLastResponseBody = json;
                    var responseText = ExtractAiChatContent(json);
                    _aiCurrentPartialResponse = responseText;
                    _aiResponse += responseText;
                    ScrollAiResponseToBottom();
                    _aiMessages.Add(new AiChatMessage("user", originalPrompt));
                    _aiMessages.Add(new AiChatMessage("assistant", (string.IsNullOrEmpty(toolResults) ? "" : toolResults + "\n\n") + responseText));
                    _aiLog = T("请求完成", "Request completed");
                },
                ex =>
                {
                    if (!IsCurrentAiRun(runId))
                        return;
                    var errorText = T("请求失败: ", "Request failed: ") + ex.Message;
                    _aiResponse += errorText;
                    _aiLog = errorText;
                    RecordAiInterruptedContext(originalPrompt, "final_response", toolResults, "", errorText);
                },
                () => { if (IsCurrentAiRun(runId)) _aiSendInProgress = false; });
            return;
        }

        var streamedResponse = new StringBuilder();
        RunAiStreamTask(
            () => AiPostStreamAsync(BuildAiEndpoint(apiBase, "chat/completions"), apiKey, followupBody, _aiHttpTimeoutSeconds, GetAiCancellationToken(), delta =>
            {
                if (!IsCurrentAiRun(runId))
                    return;
                if (string.IsNullOrEmpty(delta))
                    return;
                streamedResponse.Append(delta);
                _aiCurrentPartialResponse = streamedResponse.ToString();
                _aiResponse += delta;
                ScrollAiResponseToBottom();
                _aiLog = T("正在接收...", "Receiving...");
            }),
            result =>
            {
                if (!IsCurrentAiRun(runId))
                    return;
                _aiLastResponseBody = result.RawBody;
                var responseText = streamedResponse.Length > 0 ? streamedResponse.ToString() : result.ResponseText;
                _aiCurrentPartialResponse = responseText;
                if (streamedResponse.Length == 0 && !string.IsNullOrEmpty(responseText))
                {
                    _aiResponse += responseText;
                    ScrollAiResponseToBottom();
                }
                _aiMessages.Add(new AiChatMessage("user", originalPrompt));
                _aiMessages.Add(new AiChatMessage("assistant", (string.IsNullOrEmpty(toolResults) ? "" : toolResults + "\n\n") + responseText));
                _aiLog = T("请求完成", "Request completed");
            },
            ex =>
            {
                if (!IsCurrentAiRun(runId))
                    return;
                var errorText = T("请求失败: ", "Request failed: ") + ex.Message;
                if (streamedResponse.Length == 0)
                    _aiResponse += errorText;
                else
                    _aiResponse += "\n" + errorText;
                _aiLog = errorText;
                RecordAiInterruptedContext(originalPrompt, "final_response_stream", toolResults, streamedResponse.ToString(), errorText);
            },
            () => { if (IsCurrentAiRun(runId)) _aiSendInProgress = false; });
    }
}
