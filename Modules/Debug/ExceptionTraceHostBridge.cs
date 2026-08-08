using System;
using System.Collections;

public sealed partial class ElinModifierPlugin
{
    internal void CaptureDebugErrorLog(
        string channel,
        string sourceName,
        string level,
        string message,
        string stackTrace,
        Exception exception) =>
        _modules.ExceptionTrace.CaptureDebugErrorLog(
            channel,
            sourceName,
            level,
            message,
            stackTrace,
            exception);

    private string GetDebugExceptionTraceRecordLabel() =>
        _modules.ExceptionTrace.GetDebugExceptionTraceRecordLabel();

    private void SelectDebugExceptionTraceRecord(int index) =>
        _modules.ExceptionTrace.SelectDebugExceptionTraceRecord(index);

    private void ClearDebugExceptionTraceRecords() =>
        _modules.ExceptionTrace.ClearDebugExceptionTraceRecords();

    private static string ExtractDebugStackTraceFromLogText(string text) =>
        ExceptionTraceModule.ExtractDebugStackTraceFromLogText(text);

    private static void RecordDebugSubmoduleTraceEvent(
        string method,
        object target,
        object result,
        Exception exception,
        params object[] arguments) =>
        ExceptionTraceModule.RecordDebugSubmoduleTraceEvent(
            method,
            target,
            result,
            exception,
            arguments);

    private static string DescribeDebugTraceValue(object value) =>
        ExceptionTraceModule.DescribeDebugTraceValue(value);

    private static int CountDebugNullItems(IEnumerable enumerable) =>
        ExceptionTraceModule.CountDebugNullItems(enumerable);

    private static int CountDebugCollectionItems(object collection, int maxCount) =>
        ExceptionTraceModule.CountDebugCollectionItems(collection, maxCount);
}
