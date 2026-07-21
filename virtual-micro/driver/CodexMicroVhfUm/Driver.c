#include "Driver.h"
#include <initguid.h>

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

static UCHAR MicroReportDescriptor[] = {
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

// A separate VHF child for native confirmation dialogs.  The control IOCTL
// below only accepts Tab and Enter.
static UCHAR DialogKeyboardReportDescriptor[] = {
    0x05, 0x01,             // Usage Page (Generic Desktop)
    0x09, 0x06,             // Usage (Keyboard)
    0xA1, 0x01,             // Collection (Application)
    0x85, 0x07,             // Report ID 7
    0x05, 0x07,             // Usage Page (Keyboard)
    0x19, 0xE0,             // Usage Minimum (Left Control)
    0x29, 0xE7,             // Usage Maximum (Right GUI)
    0x15, 0x00,             // Logical Minimum 0
    0x25, 0x01,             // Logical Maximum 1
    0x75, 0x01,             // Report Size 1
    0x95, 0x08,             // Report Count 8
    0x81, 0x02,             // Input (Data, Var, Abs)
    0x95, 0x01,             // Report Count 1
    0x75, 0x08,             // Report Size 8
    0x81, 0x01,             // Input (Const, Array, Abs)
    0x95, 0x06,             // Report Count 6
    0x75, 0x08,             // Report Size 8
    0x15, 0x00,             // Logical Minimum 0
    0x25, 0x65,             // Logical Maximum 101
    0x05, 0x07,             // Usage Page (Keyboard)
    0x19, 0x00,             // Usage Minimum 0
    0x29, 0x65,             // Usage Maximum 101
    0x81, 0x00,             // Input (Data, Array, Abs)
    0xC0,
};

static
VOID
VmicroDestroyUserModeVhf(
    _Inout_ PDEVICE_CONTEXT Context
    );

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

    AcquireSRWLockExclusive(&context->OutputLock);
    info->OutputSequence = context->OutputSequence;
    info->DroppedOutputReports = context->DroppedOutputReports;
    ReleaseSRWLockExclusive(&context->OutputLock);

    info->Flags =
        (context->KeyboardVhfHandle != NULL
            ? VMICRO_INFO_FLAG_DIALOG_KEYBOARD
            : 0U) |
        (InterlockedCompareExchange(
            &context->Stopping,
            0,
            0) == 0 && context->VhfHandle != NULL
                ? VMICRO_INFO_FLAG_READY
                : 0U);
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

    if (header->Sequence == context->LastBatchSequence ||
        header->Sequence < context->LastBatchSequence ||
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
VmicroHandleSubmitKeyboard(
    _In_ WDFDEVICE Device,
    _In_ WDFREQUEST Request
    )
{
    PDEVICE_CONTEXT context = DeviceGetContext(Device);
    PVMICRO_KEYBOARD_INPUT input = NULL;
    size_t inputLength = 0;
    NTSTATUS status;
    NTSTATUS pressStatus;
    NTSTATUS releaseStatus;
    ULONG accepted = 0;
    ULONG disposition;
    ULONG index;
    UCHAR wire[9];
    HID_XFER_PACKET packet;

    status = WdfRequestRetrieveInputBuffer(
        Request,
        sizeof(VMICRO_KEYBOARD_INPUT),
        (PVOID*)&input,
        &inputLength);
    if (!NT_SUCCESS(status)) {
        WdfRequestComplete(Request, status);
        return;
    }

    if (inputLength != sizeof(VMICRO_KEYBOARD_INPUT) ||
        input->Magic != VMICRO_PROTOCOL_MAGIC ||
        input->Version != VMICRO_PROTOCOL_VERSION ||
        input->Size != sizeof(VMICRO_KEYBOARD_INPUT) ||
        input->Sequence == 0 ||
        (input->KeyCode != VMICRO_KEYBOARD_KEY_TAB &&
         input->KeyCode != VMICRO_KEYBOARD_KEY_ENTER) ||
        (input->Modifiers != 0 &&
         input->Modifiers != VMICRO_KEYBOARD_MODIFIER_LEFT_SHIFT) ||
        (input->KeyCode == VMICRO_KEYBOARD_KEY_ENTER &&
         input->Modifiers != 0)) {
        VmicroCompleteSubmitResult(
            Request,
            input->Sequence,
            VmicroSubmitRejected,
            0,
            STATUS_INVALID_PARAMETER);
        return;
    }

    for (index = 0; index < 6U; index++) {
        if (input->Reserved[index] != 0) {
            VmicroCompleteSubmitResult(
                Request,
                input->Sequence,
                VmicroSubmitRejected,
                0,
                STATUS_INVALID_PARAMETER);
            return;
        }
    }

    if (InterlockedCompareExchange(&context->Stopping, 0, 0) != 0 ||
        context->KeyboardVhfHandle == NULL) {
        VmicroCompleteSubmitResult(
            Request,
            input->Sequence,
            VmicroSubmitNotSent,
            0,
            STATUS_DEVICE_NOT_READY);
        return;
    }

    RtlZeroMemory(wire, sizeof(wire));
    wire[0] = VMICRO_KEYBOARD_REPORT_ID;
    wire[1] = input->Modifiers;
    wire[3] = input->KeyCode;
    packet.reportId = VMICRO_KEYBOARD_REPORT_ID;
    packet.reportBuffer = wire;
    packet.reportBufferLen = (ULONG)sizeof(wire);
    pressStatus = VhfReadReportSubmit(context->KeyboardVhfHandle, &packet);
    if (NT_SUCCESS(pressStatus)) {
        accepted++;
    }

    // Always submit a neutral report, even if key-down failed, so an
    // uncertain completion cannot leave a modifier or key logically held.
    RtlZeroMemory(wire + 1, sizeof(wire) - 1);
    releaseStatus = VhfReadReportSubmit(context->KeyboardVhfHandle, &packet);
    if (NT_SUCCESS(releaseStatus)) {
        accepted++;
    }

    disposition = accepted == 2
        ? VmicroSubmitAccepted
        : accepted == 0
            ? VmicroSubmitNotSent
            : VmicroSubmitOutcomeUnknown;
    VmicroCompleteSubmitResult(
        Request,
        input->Sequence,
        disposition,
        accepted,
        !NT_SUCCESS(pressStatus) ? pressStatus : releaseStatus);
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

    AcquireSRWLockExclusive(&context->OutputLock);
    if (context->OutputCount == 0) {
        ReleaseSRWLockExclusive(&context->OutputLock);
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
    ReleaseSRWLockExclusive(&context->OutputLock);
    WdfRequestCompleteWithInformation(Request, STATUS_SUCCESS, sizeof(*output));
}

static
NTSTATUS
VmicroOpenLocalTarget(
    _In_ WDFDEVICE Device,
    _Out_ WDFIOTARGET* Target,
    _Out_ HANDLE* FileHandle
    )
{
    WDF_IO_TARGET_OPEN_PARAMS openParams;
    NTSTATUS status;

    *Target = NULL;
    *FileHandle = NULL;
    status = WdfIoTargetCreate(
        Device,
        WDF_NO_OBJECT_ATTRIBUTES,
        Target);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    WDF_IO_TARGET_OPEN_PARAMS_INIT_OPEN_BY_FILE(&openParams, NULL);
    status = WdfIoTargetOpen(*Target, &openParams);
    if (!NT_SUCCESS(status)) {
        WdfObjectDelete(*Target);
        *Target = NULL;
        return status;
    }

    *FileHandle = WdfIoTargetWdmGetTargetFileHandle(*Target);
    if (*FileHandle == NULL || *FileHandle == INVALID_HANDLE_VALUE) {
        WdfIoTargetClose(*Target);
        WdfObjectDelete(*Target);
        *Target = NULL;
        *FileHandle = NULL;
        return STATUS_INVALID_HANDLE;
    }

    return STATUS_SUCCESS;
}

static
NTSTATUS
VmicroCreateUserModeVhf(
    _In_ WDFDEVICE Device,
    _Inout_ PDEVICE_CONTEXT Context
    )
{
    VHF_CONFIG vhfConfig;
    HANDLE fileHandle;
    NTSTATUS status;

    status = VmicroOpenLocalTarget(
        Device,
        &Context->VhfIoTarget,
        &fileHandle);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    VHF_CONFIG_INIT(
        &vhfConfig,
        fileHandle,
        sizeof(MicroReportDescriptor),
        MicroReportDescriptor);
    vhfConfig.VhfClientContext = Context;
    vhfConfig.VendorID = VMICRO_VENDOR_ID;
    vhfConfig.ProductID = VMICRO_VENDOR_PRODUCT_ID;
    vhfConfig.VersionNumber = VMICRO_USB_RELEASE_NUMBER;
    vhfConfig.EvtVhfAsyncOperationWriteReport = VmicroEvtVhfWriteReport;

    status = VhfCreate(&vhfConfig, &Context->VhfHandle);
    if (!NT_SUCCESS(status)) {
        Context->VhfHandle = NULL;
        goto Failure;
    }

    status = VhfStart(Context->VhfHandle);
    if (!NT_SUCCESS(status)) {
        goto Failure;
    }

    status = VmicroOpenLocalTarget(
        Device,
        &Context->KeyboardVhfIoTarget,
        &fileHandle);
    if (!NT_SUCCESS(status)) {
        goto Failure;
    }

    VHF_CONFIG_INIT(
        &vhfConfig,
        fileHandle,
        sizeof(DialogKeyboardReportDescriptor),
        DialogKeyboardReportDescriptor);
    vhfConfig.VhfClientContext = Context;
    vhfConfig.VendorID = VMICRO_VENDOR_ID;
    vhfConfig.ProductID = VMICRO_DIALOG_KEYBOARD_PRODUCT_ID;
    vhfConfig.VersionNumber = VMICRO_USB_RELEASE_NUMBER;

    status = VhfCreate(&vhfConfig, &Context->KeyboardVhfHandle);
    if (!NT_SUCCESS(status)) {
        Context->KeyboardVhfHandle = NULL;
        goto Failure;
    }

    status = VhfStart(Context->KeyboardVhfHandle);
    if (!NT_SUCCESS(status)) {
        goto Failure;
    }

    return STATUS_SUCCESS;

Failure:
    VmicroDestroyUserModeVhf(Context);
    return status;
}

static
VOID
VmicroDestroyUserModeVhf(
    _Inout_ PDEVICE_CONTEXT Context
    )
{
    VHFHANDLE handle;
    VHFHANDLE keyboardHandle;
    WDFIOTARGET ioTarget;
    WDFIOTARGET keyboardIoTarget;

    InterlockedExchange(&Context->Stopping, 1);
    keyboardHandle = Context->KeyboardVhfHandle;
    Context->KeyboardVhfHandle = NULL;
    if (keyboardHandle != NULL) {
        VhfDelete(keyboardHandle, TRUE);
    }

    handle = Context->VhfHandle;
    Context->VhfHandle = NULL;
    if (handle != NULL) {
        VhfDelete(handle, TRUE);
    }

    keyboardIoTarget = Context->KeyboardVhfIoTarget;
    Context->KeyboardVhfIoTarget = NULL;
    if (keyboardIoTarget != NULL) {
        WdfIoTargetClose(keyboardIoTarget);
        WdfObjectDelete(keyboardIoTarget);
    }

    ioTarget = Context->VhfIoTarget;
    Context->VhfIoTarget = NULL;
    if (ioTarget != NULL) {
        WdfIoTargetClose(ioTarget);
        WdfObjectDelete(ioTarget);
    }
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
    WDF_OBJECT_ATTRIBUTES attributes;
    WDF_IO_QUEUE_CONFIG queueConfig;
    WDF_PNPPOWER_EVENT_CALLBACKS pnpPowerCallbacks;
    WDFDEVICE device;
    PDEVICE_CONTEXT context;
    NTSTATUS status;

    UNREFERENCED_PARAMETER(Driver);

    WDF_PNPPOWER_EVENT_CALLBACKS_INIT(&pnpPowerCallbacks);
    pnpPowerCallbacks.EvtDevicePrepareHardware =
        VmicroEvtDevicePrepareHardware;
    pnpPowerCallbacks.EvtDeviceReleaseHardware =
        VmicroEvtDeviceReleaseHardware;
    WdfDeviceInitSetPnpPowerEventCallbacks(
        DeviceInit,
        &pnpPowerCallbacks);

    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&attributes, DEVICE_CONTEXT);
    attributes.EvtCleanupCallback = VmicroEvtDeviceCleanup;
    status = WdfDeviceCreate(&DeviceInit, &attributes, &device);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    context = DeviceGetContext(device);
    InitializeSRWLock(&context->OutputLock);

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

    return STATUS_SUCCESS;
}

NTSTATUS
VmicroEvtDevicePrepareHardware(
    _In_ WDFDEVICE Device,
    _In_ WDFCMRESLIST ResourcesRaw,
    _In_ WDFCMRESLIST ResourcesTranslated
    )
{
    PDEVICE_CONTEXT context = DeviceGetContext(Device);
    LARGE_INTEGER performanceCounter;

    UNREFERENCED_PARAMETER(ResourcesRaw);
    UNREFERENCED_PARAMETER(ResourcesTranslated);

    QueryPerformanceCounter(&performanceCounter);
    context->ConnectionEpoch =
        ((UINT64)performanceCounter.QuadPart) ^ GetTickCount64();
    context->LastBatchSequence = 0;
    context->LastBatchReportCount = 0;
    context->LastBatchDisposition = 0;
    context->LastBatchAcceptedReports = 0;
    context->LastBatchStatus = STATUS_SUCCESS;

    AcquireSRWLockExclusive(&context->OutputLock);
    context->OutputSequence = 0;
    context->DroppedOutputReports = 0;
    context->OutputHead = 0;
    context->OutputTail = 0;
    context->OutputCount = 0;
    ReleaseSRWLockExclusive(&context->OutputLock);

    InterlockedExchange(&context->Stopping, 0);
    return VmicroCreateUserModeVhf(Device, context);
}

NTSTATUS
VmicroEvtDeviceReleaseHardware(
    _In_ WDFDEVICE Device,
    _In_ WDFCMRESLIST ResourcesTranslated
    )
{
    UNREFERENCED_PARAMETER(ResourcesTranslated);
    VmicroDestroyUserModeVhf(DeviceGetContext(Device));
    return STATUS_SUCCESS;
}

VOID
VmicroEvtDeviceCleanup(
    _In_ WDFOBJECT DeviceObject
    )
{
    PDEVICE_CONTEXT context = DeviceGetContext((WDFDEVICE)DeviceObject);
    VmicroDestroyUserModeVhf(context);
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
    case IOCTL_VMICRO_SUBMIT_KEYBOARD:
        VmicroHandleSubmitKeyboard(device, Request);
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
    LARGE_INTEGER performanceCounter;

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
    QueryPerformanceCounter(&performanceCounter);
    record.PerformanceCounter = (UINT64)performanceCounter.QuadPart;
    record.OriginalLength = HidTransferPacket->reportBufferLen;

    if (HidTransferPacket->reportBufferLen == VMICRO_REPORT_LENGTH &&
        HidTransferPacket->reportBuffer[0] == HidTransferPacket->reportId) {
        RtlCopyMemory(
            record.WireReport,
            HidTransferPacket->reportBuffer,
            VMICRO_REPORT_LENGTH);
        record.Flags = VMICRO_OUTPUT_FLAG_BUFFER_INCLUDED_REPORT_ID;
    }
    else if (HidTransferPacket->reportBufferLen <= VMICRO_REPORT_LENGTH - 1) {
        record.WireReport[0] = HidTransferPacket->reportId;
        copyLength = HidTransferPacket->reportBufferLen;
        RtlCopyMemory(
            record.WireReport + 1,
            HidTransferPacket->reportBuffer,
            copyLength);
        record.Flags = VMICRO_OUTPUT_FLAG_BUFFER_EXCLUDED_REPORT_ID;
    }
    else {
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

    AcquireSRWLockExclusive(&context->OutputLock);
    if (context->OutputCount == VMICRO_OUTPUT_QUEUE_CAPACITY) {
        context->DroppedOutputReports++;
        ReleaseSRWLockExclusive(&context->OutputLock);
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
    ReleaseSRWLockExclusive(&context->OutputLock);

Complete:
    (VOID)VhfAsyncOperationComplete(VhfOperationHandle, status);
}
