#include <initguid.h>
#include "Driver.h"

DEFINE_GUID(
    GUID_DEVINTERFACE_CODEX_MICRO_VHF,
    0xe2a7cb54,
    0x8420,
    0x4d51,
    0x9d,
    0xd8,
    0xd6,
    0x57,
    0x5b,
    0x92,
    0x51,
    0xd1);

static const UCHAR MicroReportDescriptor[] = {
    0x06, 0x00, 0xFF,       // Usage Page (Vendor 0xFF00)
    0x09, 0x01,             // Usage 1
    0xA1, 0x01,             // Collection (Application)
    0x85, 0x06,             // Report ID 6
    0x15, 0x00,             // Logical Minimum 0
    0x26, 0xFF, 0x00,       // Logical Maximum 255
    0x75, 0x08,             // Report Size 8
    0x95, 0x3F,             // Report Count 63
    0x09, 0x01,
    0x81, 0x02,             // Input (Data, Var, Abs)
    0x95, 0x3F,
    0x09, 0x01,
    0x91, 0x02,             // Output (Data, Var, Abs)
    0xC0,
};

static
VOID
VmicroCompleteSubmitResult(
    _In_ WDFREQUEST Request,
    _In_ UINT64 Sequence,
    _In_ ULONG Disposition,
    _In_ ULONG AcceptedReports,
    _In_ NTSTATUS FirstFailureStatus
    )
{
    PVMICRO_SUBMIT_RESULT result = NULL;
    NTSTATUS status = WdfRequestRetrieveOutputBuffer(
        Request,
        sizeof(VMICRO_SUBMIT_RESULT),
        (PVOID*)&result,
        NULL);

    if (!NT_SUCCESS(status)) {
        WdfRequestComplete(Request, status);
        return;
    }

    RtlZeroMemory(result, sizeof(*result));
    result->Magic = VMICRO_PROTOCOL_MAGIC;
    result->Version = VMICRO_PROTOCOL_VERSION;
    result->Size = sizeof(*result);
    result->Sequence = Sequence;
    result->Disposition = Disposition;
    result->AcceptedReports = AcceptedReports;
    result->FirstFailureStatus = FirstFailureStatus;
    WdfRequestCompleteWithInformation(
        Request,
        STATUS_SUCCESS,
        sizeof(*result));
}

static
BOOLEAN
VmicroValidateWireReport(
    _In_reads_bytes_(VMICRO_REPORT_LENGTH) const UCHAR* Report
    )
{
    ULONG index;
    UCHAR payloadLength;

    if (Report[0] != 0x06 || Report[1] != 0x02) {
        return FALSE;
    }

    payloadLength = Report[2];
    if (payloadLength > 61) {
        return FALSE;
    }

    for (index = 3U + payloadLength; index < VMICRO_REPORT_LENGTH; index++) {
        if (Report[index] != 0) {
            return FALSE;
        }
    }

    return TRUE;
}

static
VOID
VmicroHandleGetInfo(
    _In_ WDFDEVICE Device,
    _In_ WDFREQUEST Request
    )
{
    PDEVICE_CONTEXT context = DeviceGetContext(Device);
    PVMICRO_INFO info = NULL;
    NTSTATUS status = WdfRequestRetrieveOutputBuffer(
        Request,
        sizeof(VMICRO_INFO),
        (PVOID*)&info,
        NULL);

    if (!NT_SUCCESS(status)) {
        WdfRequestComplete(Request, status);
        return;
    }

    RtlZeroMemory(info, sizeof(*info));
    info->Magic = VMICRO_PROTOCOL_MAGIC;
    info->Version = VMICRO_PROTOCOL_VERSION;
    info->Size = sizeof(*info);
    info->ConnectionEpoch = context->ConnectionEpoch;
    info->LastBatchSequence = context->LastBatchSequence;

    WdfSpinLockAcquire(context->OutputLock);
    info->OutputSequence = context->OutputSequence;
    info->DroppedOutputReports = context->DroppedOutputReports;
    WdfSpinLockRelease(context->OutputLock);

    info->Flags = InterlockedCompareExchange(
        &context->Stopping,
        0,
        0) == 0 ? 1U : 0U;
    WdfRequestCompleteWithInformation(Request, STATUS_SUCCESS, sizeof(*info));
}

static
VOID
VmicroHandleSubmitInput(
    _In_ WDFDEVICE Device,
    _In_ WDFREQUEST Request
    )
{
    PDEVICE_CONTEXT context = DeviceGetContext(Device);
    PVMICRO_BATCH_HEADER header = NULL;
    size_t inputLength = 0;
    size_t expectedLength;
    NTSTATUS status;
    ULONG index;
    ULONG accepted = 0;
    ULONG disposition;
    NTSTATUS firstFailure = STATUS_SUCCESS;
    PUCHAR reports;

    status = WdfRequestRetrieveInputBuffer(
        Request,
        sizeof(VMICRO_BATCH_HEADER),
        (PVOID*)&header,
        &inputLength);
    if (!NT_SUCCESS(status)) {
        WdfRequestComplete(Request, status);
        return;
    }

    if (header->Magic != VMICRO_PROTOCOL_MAGIC ||
        header->Version != VMICRO_PROTOCOL_VERSION ||
        header->Sequence == 0 ||
        header->ReportCount == 0 ||
        header->ReportCount > VMICRO_MAX_BATCH_REPORTS) {
        VmicroCompleteSubmitResult(
            Request,
            header->Sequence,
            VmicroSubmitRejected,
            0,
            STATUS_INVALID_PARAMETER);
        return;
    }

    expectedLength = sizeof(VMICRO_BATCH_HEADER) +
        ((size_t)header->ReportCount * VMICRO_REPORT_LENGTH);
    if (inputLength != expectedLength) {
        VmicroCompleteSubmitResult(
            Request,
            header->Sequence,
            VmicroSubmitRejected,
            0,
            STATUS_INFO_LENGTH_MISMATCH);
        return;
    }

    if (header->Sequence == context->LastBatchSequence &&
        header->ReportCount == context->LastBatchReportCount) {
        VmicroCompleteSubmitResult(
            Request,
            header->Sequence,
            VmicroSubmitDuplicate,
            context->LastBatchAcceptedReports,
            context->LastBatchStatus);
        return;
    }

    if (header->Sequence == context->LastBatchSequence) {
        VmicroCompleteSubmitResult(
            Request,
            header->Sequence,
            VmicroSubmitRejected,
            0,
            STATUS_INVALID_PARAMETER);
        return;
    }

    if (header->Sequence < context->LastBatchSequence ||
        InterlockedCompareExchange(&context->Stopping, 0, 0) != 0 ||
        context->VhfHandle == NULL) {
        VmicroCompleteSubmitResult(
            Request,
            header->Sequence,
            VmicroSubmitRejected,
            0,
            STATUS_DEVICE_NOT_READY);
        return;
    }

    reports = ((PUCHAR)header) + sizeof(*header);
    for (index = 0; index < header->ReportCount; index++) {
        if (!VmicroValidateWireReport(
            reports + ((size_t)index * VMICRO_REPORT_LENGTH))) {
            VmicroCompleteSubmitResult(
                Request,
                header->Sequence,
                VmicroSubmitRejected,
                0,
                STATUS_INVALID_PARAMETER);
            return;
        }
    }

    // HID report buffers include the report ID at byte 0. reportId repeats
    // that value for routing; it does not remove the byte from reportBuffer.
    // The descriptor declares 63 data bytes, therefore HidP exposes a 64-byte
    // input report including ID 0x06.
    for (index = 0; index < header->ReportCount; index++) {
        PUCHAR wire = reports + ((size_t)index * VMICRO_REPORT_LENGTH);
        HID_XFER_PACKET packet;

        packet.reportId = wire[0];
        packet.reportBuffer = wire;
        packet.reportBufferLen = VMICRO_REPORT_LENGTH;
        status = VhfReadReportSubmit(context->VhfHandle, &packet);
        if (!NT_SUCCESS(status)) {
            firstFailure = status;
            break;
        }

        accepted++;
    }

    disposition = accepted == header->ReportCount
        ? VmicroSubmitAccepted
        : accepted == 0
            ? VmicroSubmitNotSent
            : VmicroSubmitOutcomeUnknown;

    context->LastBatchSequence = header->Sequence;
    context->LastBatchReportCount = header->ReportCount;
    context->LastBatchDisposition = disposition;
    context->LastBatchAcceptedReports = accepted;
    context->LastBatchStatus = firstFailure;
    VmicroCompleteSubmitResult(
        Request,
        header->Sequence,
        disposition,
        accepted,
        firstFailure);
}

static
VOID
VmicroHandleReadOutput(
    _In_ WDFDEVICE Device,
    _In_ WDFREQUEST Request
    )
{
    PDEVICE_CONTEXT context = DeviceGetContext(Device);
    PVMICRO_OUTPUT_RECORD output = NULL;
    NTSTATUS status = WdfRequestRetrieveOutputBuffer(
        Request,
        sizeof(VMICRO_OUTPUT_RECORD),
        (PVOID*)&output,
        NULL);

    if (!NT_SUCCESS(status)) {
        WdfRequestComplete(Request, status);
        return;
    }

    WdfSpinLockAcquire(context->OutputLock);
    if (context->OutputCount == 0) {
        WdfSpinLockRelease(context->OutputLock);
        WdfRequestComplete(Request, STATUS_NO_MORE_ENTRIES);
        return;
    }

    RtlCopyMemory(
        output,
        &context->OutputQueue[context->OutputHead],
        sizeof(*output));
    context->OutputHead =
        (context->OutputHead + 1U) % VMICRO_OUTPUT_QUEUE_CAPACITY;
    context->OutputCount--;
    WdfSpinLockRelease(context->OutputLock);
    WdfRequestCompleteWithInformation(Request, STATUS_SUCCESS, sizeof(*output));
}

NTSTATUS
DriverEntry(
    _In_ PDRIVER_OBJECT DriverObject,
    _In_ PUNICODE_STRING RegistryPath
    )
{
    WDF_DRIVER_CONFIG config;
    WDF_DRIVER_CONFIG_INIT(&config, VmicroEvtDeviceAdd);
    return WdfDriverCreate(
        DriverObject,
        RegistryPath,
        WDF_NO_OBJECT_ATTRIBUTES,
        &config,
        WDF_NO_HANDLE);
}

NTSTATUS
VmicroEvtDeviceAdd(
    _In_ WDFDRIVER Driver,
    _Inout_ PWDFDEVICE_INIT DeviceInit
    )
{
    UNREFERENCED_PARAMETER(Driver);

    DECLARE_CONST_UNICODE_STRING(
        securityDescriptor,
        L"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGW;;;IU)");
    WDF_OBJECT_ATTRIBUTES attributes;
    WDF_OBJECT_ATTRIBUTES lockAttributes;
    WDF_IO_QUEUE_CONFIG queueConfig;
    WDFDEVICE device;
    PDEVICE_CONTEXT context;
    VHF_CONFIG vhfConfig;
    LARGE_INTEGER performanceCounter;
    NTSTATUS status;

    WdfDeviceInitSetExclusive(DeviceInit, TRUE);
    WdfDeviceInitSetIoType(DeviceInit, WdfDeviceIoBuffered);
    status = WdfDeviceInitAssignSDDLString(DeviceInit, &securityDescriptor);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&attributes, DEVICE_CONTEXT);
    attributes.EvtCleanupCallback = VmicroEvtDeviceCleanup;
    attributes.ExecutionLevel = WdfExecutionLevelPassive;
    status = WdfDeviceCreate(&DeviceInit, &attributes, &device);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    context = DeviceGetContext(device);
    RtlZeroMemory(context, sizeof(*context));
    performanceCounter = KeQueryPerformanceCounter(NULL);
    context->ConnectionEpoch =
        ((UINT64)performanceCounter.QuadPart) ^ KeQueryInterruptTime();

    WDF_OBJECT_ATTRIBUTES_INIT(&lockAttributes);
    lockAttributes.ParentObject = device;
    status = WdfSpinLockCreate(&lockAttributes, &context->OutputLock);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(
        &queueConfig,
        WdfIoQueueDispatchSequential);
    queueConfig.EvtIoDeviceControl = VmicroEvtIoDeviceControl;
    status = WdfIoQueueCreate(
        device,
        &queueConfig,
        WDF_NO_OBJECT_ATTRIBUTES,
        WDF_NO_HANDLE);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    status = WdfDeviceCreateDeviceInterface(
        device,
        &GUID_DEVINTERFACE_CODEX_MICRO_VHF,
        NULL);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    VHF_CONFIG_INIT(
        &vhfConfig,
        WdfDeviceWdmGetDeviceObject(device),
        sizeof(MicroReportDescriptor),
        (PUCHAR)MicroReportDescriptor);
    vhfConfig.VhfClientContext = context;
    vhfConfig.VendorID = 0x303A;
    vhfConfig.ProductID = 0x8360;
    vhfConfig.VersionNumber = 0x0100;
    vhfConfig.EvtVhfAsyncOperationWriteReport = VmicroEvtVhfWriteReport;

    status = VhfCreate(&vhfConfig, &context->VhfHandle);
    if (!NT_SUCCESS(status)) {
        context->VhfHandle = NULL;
        return status;
    }

    status = VhfStart(context->VhfHandle);
    if (!NT_SUCCESS(status)) {
        VHFHANDLE handle = context->VhfHandle;
        context->VhfHandle = NULL;
        InterlockedExchange(&context->Stopping, 1);
        VhfDelete(handle, TRUE);
        return status;
    }

    return STATUS_SUCCESS;
}

VOID
VmicroEvtDeviceCleanup(
    _In_ WDFOBJECT DeviceObject
    )
{
    PDEVICE_CONTEXT context = DeviceGetContext((WDFDEVICE)DeviceObject);
    VHFHANDLE handle;

    InterlockedExchange(&context->Stopping, 1);
    handle = context->VhfHandle;
    context->VhfHandle = NULL;
    if (handle != NULL) {
        // Microsoft requires synchronous deletion at PASSIVE_LEVEL; FALSE is
        // reserved and is intentionally never used.
        VhfDelete(handle, TRUE);
    }
}

VOID
VmicroEvtIoDeviceControl(
    _In_ WDFQUEUE Queue,
    _In_ WDFREQUEST Request,
    _In_ size_t OutputBufferLength,
    _In_ size_t InputBufferLength,
    _In_ ULONG IoControlCode
    )
{
    WDFDEVICE device = WdfIoQueueGetDevice(Queue);
    UNREFERENCED_PARAMETER(OutputBufferLength);
    UNREFERENCED_PARAMETER(InputBufferLength);

    switch (IoControlCode) {
    case IOCTL_VMICRO_GET_INFO:
        VmicroHandleGetInfo(device, Request);
        break;
    case IOCTL_VMICRO_SUBMIT_INPUT:
        VmicroHandleSubmitInput(device, Request);
        break;
    case IOCTL_VMICRO_READ_OUTPUT:
        VmicroHandleReadOutput(device, Request);
        break;
    default:
        WdfRequestComplete(Request, STATUS_INVALID_DEVICE_REQUEST);
        break;
    }
}

VOID
VmicroEvtVhfWriteReport(
    _In_ PVOID VhfClientContext,
    _In_ VHFOPERATIONHANDLE VhfOperationHandle,
    _In_opt_ PVOID VhfOperationContext,
    _In_ PHID_XFER_PACKET HidTransferPacket
    )
{
    PDEVICE_CONTEXT context = (PDEVICE_CONTEXT)VhfClientContext;
    VMICRO_OUTPUT_RECORD record;
    NTSTATUS status = STATUS_SUCCESS;
    ULONG copyLength;

    UNREFERENCED_PARAMETER(VhfOperationContext);
    RtlZeroMemory(&record, sizeof(record));

    if (context == NULL ||
        InterlockedCompareExchange(&context->Stopping, 0, 0) != 0) {
        status = STATUS_DELETE_PENDING;
        goto Complete;
    }

    if (HidTransferPacket == NULL ||
        HidTransferPacket->reportBuffer == NULL ||
        HidTransferPacket->reportId != 0x06 ||
        HidTransferPacket->reportBufferLen == 0) {
        status = STATUS_INVALID_PARAMETER;
        goto Complete;
    }

    record.Magic = VMICRO_PROTOCOL_MAGIC;
    record.Version = VMICRO_PROTOCOL_VERSION;
    record.Size = sizeof(record);
    record.PerformanceCounter =
        (UINT64)KeQueryPerformanceCounter(NULL).QuadPart;
    record.OriginalLength = HidTransferPacket->reportBufferLen;

    if (HidTransferPacket->reportBufferLen == VMICRO_REPORT_LENGTH &&
        HidTransferPacket->reportBuffer[0] == HidTransferPacket->reportId) {
        RtlCopyMemory(
            record.WireReport,
            HidTransferPacket->reportBuffer,
            VMICRO_REPORT_LENGTH);
        record.Flags = VMICRO_OUTPUT_FLAG_BUFFER_INCLUDED_REPORT_ID;
    } else if (HidTransferPacket->reportBufferLen <= VMICRO_REPORT_LENGTH - 1) {
        record.WireReport[0] = HidTransferPacket->reportId;
        copyLength = HidTransferPacket->reportBufferLen;
        RtlCopyMemory(
            record.WireReport + 1,
            HidTransferPacket->reportBuffer,
            copyLength);
        record.Flags = VMICRO_OUTPUT_FLAG_BUFFER_EXCLUDED_REPORT_ID;
    } else {
        status = STATUS_INVALID_BUFFER_SIZE;
        goto Complete;
    }

    if (record.WireReport[1] != 0x01 && record.WireReport[1] != 0x02) {
        status = STATUS_INVALID_PARAMETER;
        goto Complete;
    }

    if (record.WireReport[2] > 61) {
        status = STATUS_INVALID_BUFFER_SIZE;
        goto Complete;
    }

    WdfSpinLockAcquire(context->OutputLock);
    if (context->OutputCount == VMICRO_OUTPUT_QUEUE_CAPACITY) {
        context->DroppedOutputReports++;
        WdfSpinLockRelease(context->OutputLock);
        status = STATUS_DEVICE_BUSY;
        goto Complete;
    }

    record.Sequence = ++context->OutputSequence;
    RtlCopyMemory(
        &context->OutputQueue[context->OutputTail],
        &record,
        sizeof(record));
    context->OutputTail =
        (context->OutputTail + 1U) % VMICRO_OUTPUT_QUEUE_CAPACITY;
    context->OutputCount++;
    WdfSpinLockRelease(context->OutputLock);

Complete:
    // Every VHF operation is completed exactly once, including backpressure
    // and validation failures. JSON parsing never occurs at callback IRQL.
    (VOID)VhfAsyncOperationComplete(VhfOperationHandle, status);
}
