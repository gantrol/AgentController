#include "Driver.h"

static HID_REPORT_DESCRIPTOR CodexMicroReportDescriptor[] = {
    0x06, 0x00, 0xFF,       // Usage Page (Vendor 0xFF00)
    0x09, 0x01,             // Usage 1
    0xA1, 0x01,             // Collection (Application)

    0x15, 0x00,             // Logical Minimum 0
    0x26, 0xFF, 0x00,       // Logical Maximum 255
    0x75, 0x08,             // Report Size 8

    0x85, CODEX_MICRO_REPORT_ID,
    0x95, 0x3F,             // 63 payload bytes + report ID
    0x09, 0x01,
    0x81, 0x02,             // Input (Data, Variable, Absolute)
    0x95, 0x3F,
    0x09, 0x01,
    0x91, 0x02,             // Output (Data, Variable, Absolute)

    0x85, CODEX_MICRO_INJECT_FEATURE_ID,
    0x96, 0x41, 0x00,       // status byte + complete 64-byte wire report
    0x09, 0x10,
    0xB1, 0x02,

    0x85, CODEX_MICRO_OUTPUT_FEATURE_ID,
    0x96, 0x41, 0x00,
    0x09, 0x11,
    0xB1, 0x02,

    0x85, CODEX_MICRO_INFO_FEATURE_ID,
    0x96, 0x41, 0x00,
    0x09, 0x12,
    0xB1, 0x02,

    0xC0,
};

static HID_DESCRIPTOR CodexMicroHidDescriptor = {
    0x09,
    0x21,
    0x0100,
    0x00,
    0x01,
    {
        {
            0x22,
            sizeof(CodexMicroReportDescriptor),
        },
    },
};

static
BOOLEAN
CodexMicroValidateWireReport(
    _In_reads_bytes_(CODEX_MICRO_WIRE_REPORT_LENGTH) const UCHAR* Report,
    _In_ BOOLEAN DeviceToHost
    )
{
    ULONG index;
    if (Report[0] != CODEX_MICRO_REPORT_ID ||
        (DeviceToHost
            ? Report[1] != 0x02
            : (Report[1] != 0x01 && Report[1] != 0x02)) ||
        Report[2] > 61) {
        return FALSE;
    }

    for (index = 3U + Report[2];
         index < CODEX_MICRO_WIRE_REPORT_LENGTH;
         index++) {
        if (Report[index] != 0) {
            return FALSE;
        }
    }

    return TRUE;
}

static
NTSTATUS
CodexMicroCopyNextInputReport(
    _In_ PDEVICE_CONTEXT Context,
    _In_ WDFREQUEST Request
    )
{
    UCHAR report[CODEX_MICRO_WIRE_REPORT_LENGTH];

    if (Context->InputCount == 0) {
        return STATUS_NO_MORE_ENTRIES;
    }

    RtlCopyMemory(
        report,
        Context->InputReports[Context->InputHead],
        sizeof(report));
    Context->InputHead =
        (Context->InputHead + 1U) % CODEX_MICRO_QUEUE_CAPACITY;
    Context->InputCount--;
    return RequestCopyFromBuffer(Request, report, sizeof(report));
}

static
VOID
CodexMicroCompleteOnePendingRead(
    _In_ PDEVICE_CONTEXT Context
    )
{
    WDFREQUEST request;
    NTSTATUS status;

    if (Context->InputCount == 0) {
        return;
    }

    status = WdfIoQueueRetrieveNextRequest(
        Context->ManualReadQueue,
        &request);
    if (!NT_SUCCESS(status)) {
        return;
    }

    status = CodexMicroCopyNextInputReport(Context, request);
    WdfRequestComplete(request, status);
}

static
NTSTATUS
CodexMicroQueueInputReport(
    _In_ PDEVICE_CONTEXT Context,
    _In_reads_bytes_(CODEX_MICRO_WIRE_REPORT_LENGTH) const UCHAR* Report
    )
{
    if (!CodexMicroValidateWireReport(Report, TRUE)) {
        return STATUS_INVALID_PARAMETER;
    }

    if (Context->InputCount == CODEX_MICRO_QUEUE_CAPACITY) {
        Context->DroppedInputReports++;
        return STATUS_DEVICE_BUSY;
    }

    RtlCopyMemory(
        Context->InputReports[Context->InputTail],
        Report,
        CODEX_MICRO_WIRE_REPORT_LENGTH);
    Context->InputTail =
        (Context->InputTail + 1U) % CODEX_MICRO_QUEUE_CAPACITY;
    Context->InputCount++;
    CodexMicroCompleteOnePendingRead(Context);
    return STATUS_SUCCESS;
}

static
NTSTATUS
CodexMicroQueueOutputReport(
    _In_ PDEVICE_CONTEXT Context,
    _In_reads_bytes_(CODEX_MICRO_WIRE_REPORT_LENGTH) const UCHAR* Report
    )
{
    if (!CodexMicroValidateWireReport(Report, FALSE)) {
        return STATUS_INVALID_PARAMETER;
    }

    if (Context->OutputCount == CODEX_MICRO_QUEUE_CAPACITY) {
        Context->OutputHead =
            (Context->OutputHead + 1U) % CODEX_MICRO_QUEUE_CAPACITY;
        Context->OutputCount--;
        Context->DroppedOutputReports++;
    }

    RtlCopyMemory(
        Context->OutputReports[Context->OutputTail],
        Report,
        CODEX_MICRO_WIRE_REPORT_LENGTH);
    Context->OutputTail =
        (Context->OutputTail + 1U) % CODEX_MICRO_QUEUE_CAPACITY;
    Context->OutputCount++;
    return STATUS_SUCCESS;
}

static
NTSTATUS
CodexMicroReadReport(
    _In_ PDEVICE_CONTEXT Context,
    _In_ WDFREQUEST Request,
    _Out_ BOOLEAN* CompleteRequest
    )
{
    NTSTATUS status;

    if (Context->InputCount != 0) {
        *CompleteRequest = TRUE;
        return CodexMicroCopyNextInputReport(Context, Request);
    }

    status = WdfRequestForwardToIoQueue(
        Request,
        Context->ManualReadQueue);
    *CompleteRequest = !NT_SUCCESS(status);
    return status;
}

static
NTSTATUS
CodexMicroWriteReport(
    _In_ PDEVICE_CONTEXT Context,
    _In_ WDFREQUEST Request
    )
{
    HID_XFER_PACKET packet;
    UCHAR report[CODEX_MICRO_WIRE_REPORT_LENGTH];
    NTSTATUS status = RequestGetHidXferPacketToWrite(Request, &packet);

    if (!NT_SUCCESS(status)) {
        return status;
    }

    if (packet.reportId != CODEX_MICRO_REPORT_ID ||
        packet.reportBuffer == NULL) {
        return STATUS_INVALID_PARAMETER;
    }

    RtlZeroMemory(report, sizeof(report));
    if (packet.reportBufferLen >= CODEX_MICRO_WIRE_REPORT_LENGTH &&
        packet.reportBuffer[0] == CODEX_MICRO_REPORT_ID) {
        RtlCopyMemory(report, packet.reportBuffer, sizeof(report));
    } else if (packet.reportBufferLen >= CODEX_MICRO_WIRE_REPORT_LENGTH - 1U) {
        report[0] = CODEX_MICRO_REPORT_ID;
        RtlCopyMemory(
            report + 1,
            packet.reportBuffer,
            CODEX_MICRO_WIRE_REPORT_LENGTH - 1U);
    } else {
        return STATUS_INVALID_BUFFER_SIZE;
    }

    status = CodexMicroQueueOutputReport(Context, report);
    if (NT_SUCCESS(status)) {
        WdfRequestSetInformation(
            Request,
            CODEX_MICRO_WIRE_REPORT_LENGTH);
    }

    return status;
}

static
NTSTATUS
CodexMicroGetFeature(
    _In_ PDEVICE_CONTEXT Context,
    _In_ WDFREQUEST Request
    )
{
    HID_XFER_PACKET packet;
    NTSTATUS status = RequestGetHidXferPacketToRead(Request, &packet);
    UCHAR report[CODEX_MICRO_FEATURE_REPORT_LENGTH];

    if (!NT_SUCCESS(status)) {
        return status;
    }

    if (packet.reportBuffer == NULL ||
        packet.reportBufferLen < CODEX_MICRO_FEATURE_REPORT_LENGTH) {
        return STATUS_INVALID_BUFFER_SIZE;
    }

    RtlZeroMemory(report, sizeof(report));
    report[0] = packet.reportId;

    switch (packet.reportId) {
    case CODEX_MICRO_OUTPUT_FEATURE_ID:
        if (Context->OutputCount != 0) {
            report[1] = 1;
            RtlCopyMemory(
                report + 2,
                Context->OutputReports[Context->OutputHead],
                CODEX_MICRO_WIRE_REPORT_LENGTH);
            Context->OutputHead =
                (Context->OutputHead + 1U) % CODEX_MICRO_QUEUE_CAPACITY;
            Context->OutputCount--;
        }
        break;

    case CODEX_MICRO_INFO_FEATURE_ID:
        RtlCopyMemory(report + 1, "CMHIDUM2", 8);
        report[9] = 2;
        report[10] = 0;
        report[11] = (UCHAR)Context->InputCount;
        report[12] = (UCHAR)Context->OutputCount;
        RtlCopyMemory(
            report + 16,
            &Context->ConnectionEpoch.QuadPart,
            sizeof(Context->ConnectionEpoch.QuadPart));
        RtlCopyMemory(
            report + 24,
            &Context->DroppedInputReports,
            sizeof(Context->DroppedInputReports));
        RtlCopyMemory(
            report + 28,
            &Context->DroppedOutputReports,
            sizeof(Context->DroppedOutputReports));
        break;

    default:
        return STATUS_INVALID_PARAMETER;
    }

    RtlCopyMemory(packet.reportBuffer, report, sizeof(report));
    WdfRequestSetInformation(Request, sizeof(report));
    return STATUS_SUCCESS;
}

static
NTSTATUS
CodexMicroSetFeature(
    _In_ PDEVICE_CONTEXT Context,
    _In_ WDFREQUEST Request
    )
{
    HID_XFER_PACKET packet;
    NTSTATUS status = RequestGetHidXferPacketToWrite(Request, &packet);

    if (!NT_SUCCESS(status)) {
        return status;
    }

    if (packet.reportId != CODEX_MICRO_INJECT_FEATURE_ID ||
        packet.reportBuffer == NULL) {
        return STATUS_INVALID_PARAMETER;
    }

    if (packet.reportBufferLen < CODEX_MICRO_FEATURE_REPORT_LENGTH ||
        packet.reportBuffer[0] != CODEX_MICRO_INJECT_FEATURE_ID) {
        return STATUS_INVALID_BUFFER_SIZE;
    }

    status = CodexMicroQueueInputReport(Context, packet.reportBuffer + 2);
    if (NT_SUCCESS(status)) {
        WdfRequestSetInformation(
            Request,
            CODEX_MICRO_FEATURE_REPORT_LENGTH);
    }

    return status;
}

static
NTSTATUS
CodexMicroGetStringId(
    _In_ WDFREQUEST Request,
    _Out_ ULONG* StringId,
    _Out_ ULONG* LanguageId
    )
{
    WDFMEMORY memory;
    size_t length;
    ULONG* input;
    NTSTATUS status = WdfRequestRetrieveInputMemory(Request, &memory);

    if (!NT_SUCCESS(status)) {
        return status;
    }

    input = (ULONG*)WdfMemoryGetBuffer(memory, &length);
    if (input == NULL || length < sizeof(ULONG)) {
        return STATUS_INVALID_BUFFER_SIZE;
    }

    *StringId = *input & 0xFFFFU;
    *LanguageId = *input >> 16;
    return STATUS_SUCCESS;
}

static
NTSTATUS
CodexMicroGetString(
    _In_ WDFREQUEST Request,
    _In_ BOOLEAN Indexed
    )
{
    ULONG stringId;
    ULONG languageId;
    const WCHAR* value;
    size_t length;
    NTSTATUS status = CodexMicroGetStringId(
        Request,
        &stringId,
        &languageId);

    UNREFERENCED_PARAMETER(languageId);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    if (Indexed) {
        if (stringId != CODEX_MICRO_INDEXED_STRING_ID) {
            return STATUS_INVALID_PARAMETER;
        }
        value = CODEX_MICRO_INDEXED_STRING;
        length = sizeof(CODEX_MICRO_INDEXED_STRING);
    } else {
        switch (stringId) {
        case HID_STRING_ID_IMANUFACTURER:
            value = CODEX_MICRO_MANUFACTURER_STRING;
            length = sizeof(CODEX_MICRO_MANUFACTURER_STRING);
            break;
        case HID_STRING_ID_IPRODUCT:
            value = CODEX_MICRO_PRODUCT_STRING;
            length = sizeof(CODEX_MICRO_PRODUCT_STRING);
            break;
        case HID_STRING_ID_ISERIALNUMBER:
            value = CODEX_MICRO_SERIAL_STRING;
            length = sizeof(CODEX_MICRO_SERIAL_STRING);
            break;
        default:
            return STATUS_INVALID_PARAMETER;
        }
    }

    return RequestCopyFromBuffer(Request, value, length);
}

static
NTSTATUS
CodexMicroCreateQueues(
    _In_ WDFDEVICE Device,
    _Inout_ PDEVICE_CONTEXT Context
    )
{
    WDF_IO_QUEUE_CONFIG queueConfig;
    WDF_OBJECT_ATTRIBUTES queueAttributes;
    PQUEUE_CONTEXT queueContext;
    NTSTATUS status;

    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(
        &queueConfig,
        WdfIoQueueDispatchSequential);
    queueConfig.EvtIoDeviceControl = CodexMicroEvtIoDeviceControl;
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(
        &queueAttributes,
        QUEUE_CONTEXT);

    status = WdfIoQueueCreate(
        Device,
        &queueConfig,
        &queueAttributes,
        &Context->DefaultQueue);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    queueContext = QueueGetContext(Context->DefaultQueue);
    queueContext->DeviceContext = Context;

    WDF_IO_QUEUE_CONFIG_INIT(
        &queueConfig,
        WdfIoQueueDispatchManual);
    status = WdfIoQueueCreate(
        Device,
        &queueConfig,
        WDF_NO_OBJECT_ATTRIBUTES,
        &Context->ManualReadQueue);
    return status;
}

NTSTATUS
DriverEntry(
    _In_ PDRIVER_OBJECT DriverObject,
    _In_ PUNICODE_STRING RegistryPath
    )
{
    WDF_DRIVER_CONFIG config;

    WDF_DRIVER_CONFIG_INIT(&config, CodexMicroEvtDeviceAdd);
    return WdfDriverCreate(
        DriverObject,
        RegistryPath,
        WDF_NO_OBJECT_ATTRIBUTES,
        &config,
        WDF_NO_HANDLE);
}

NTSTATUS
CodexMicroEvtDeviceAdd(
    _In_ WDFDRIVER Driver,
    _Inout_ PWDFDEVICE_INIT DeviceInit
    )
{
    WDF_OBJECT_ATTRIBUTES attributes;
    WDFDEVICE device;
    PDEVICE_CONTEXT context;
    NTSTATUS status;

    UNREFERENCED_PARAMETER(Driver);
    WdfFdoInitSetFilter(DeviceInit);
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(
        &attributes,
        DEVICE_CONTEXT);

    status = WdfDeviceCreate(&DeviceInit, &attributes, &device);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    context = DeviceGetContext(device);
    context->Device = device;
    context->ReportDescriptor = CodexMicroReportDescriptor;
    context->HidDescriptor = CodexMicroHidDescriptor;
    context->Attributes.Size = sizeof(HID_DEVICE_ATTRIBUTES);
    context->Attributes.VendorID = CODEX_MICRO_VENDOR_ID;
    context->Attributes.ProductID = CODEX_MICRO_PRODUCT_ID;
    context->Attributes.VersionNumber = CODEX_MICRO_VERSION;
    QueryPerformanceCounter(&context->ConnectionEpoch);

    return CodexMicroCreateQueues(device, context);
}

VOID
CodexMicroEvtIoDeviceControl(
    _In_ WDFQUEUE Queue,
    _In_ WDFREQUEST Request,
    _In_ size_t OutputBufferLength,
    _In_ size_t InputBufferLength,
    _In_ ULONG IoControlCode
    )
{
    PQUEUE_CONTEXT queueContext = QueueGetContext(Queue);
    PDEVICE_CONTEXT context = queueContext->DeviceContext;
    BOOLEAN completeRequest = TRUE;
    NTSTATUS status;

    UNREFERENCED_PARAMETER(OutputBufferLength);
    UNREFERENCED_PARAMETER(InputBufferLength);

    switch (IoControlCode) {
    case IOCTL_HID_GET_DEVICE_DESCRIPTOR:
        status = RequestCopyFromBuffer(
            Request,
            &context->HidDescriptor,
            context->HidDescriptor.bLength);
        break;
    case IOCTL_HID_GET_DEVICE_ATTRIBUTES:
        status = RequestCopyFromBuffer(
            Request,
            &context->Attributes,
            sizeof(context->Attributes));
        break;
    case IOCTL_HID_GET_REPORT_DESCRIPTOR:
        status = RequestCopyFromBuffer(
            Request,
            context->ReportDescriptor,
            context->HidDescriptor.DescriptorList[0].wReportLength);
        break;
    case IOCTL_HID_READ_REPORT:
        status = CodexMicroReadReport(context, Request, &completeRequest);
        break;
    case IOCTL_HID_WRITE_REPORT:
        status = CodexMicroWriteReport(context, Request);
        break;
    case IOCTL_UMDF_HID_GET_FEATURE:
        status = CodexMicroGetFeature(context, Request);
        break;
    case IOCTL_UMDF_HID_SET_FEATURE:
        status = CodexMicroSetFeature(context, Request);
        break;
    case IOCTL_UMDF_HID_GET_INPUT_REPORT:
        status = context->InputCount != 0
            ? CodexMicroCopyNextInputReport(context, Request)
            : STATUS_NO_MORE_ENTRIES;
        break;
    case IOCTL_UMDF_HID_SET_OUTPUT_REPORT:
        status = CodexMicroWriteReport(context, Request);
        break;
    case IOCTL_HID_GET_STRING:
        status = CodexMicroGetString(Request, FALSE);
        break;
    case IOCTL_HID_GET_INDEXED_STRING:
        status = CodexMicroGetString(Request, TRUE);
        break;
    case IOCTL_HID_ACTIVATE_DEVICE:
    case IOCTL_HID_DEACTIVATE_DEVICE:
        status = STATUS_SUCCESS;
        break;
    default:
        status = STATUS_NOT_IMPLEMENTED;
        break;
    }

    if (completeRequest) {
        WdfRequestComplete(Request, status);
    }
}

NTSTATUS
RequestCopyFromBuffer(
    _In_ WDFREQUEST Request,
    _In_reads_bytes_(Length) const VOID* Source,
    _In_ size_t Length
    )
{
    WDFMEMORY memory;
    size_t outputLength;
    NTSTATUS status = WdfRequestRetrieveOutputMemory(Request, &memory);

    if (!NT_SUCCESS(status)) {
        return status;
    }

    WdfMemoryGetBuffer(memory, &outputLength);
    if (outputLength < Length) {
        return STATUS_INVALID_BUFFER_SIZE;
    }

    status = WdfMemoryCopyFromBuffer(memory, 0, (PVOID)Source, Length);
    if (NT_SUCCESS(status)) {
        WdfRequestSetInformation(Request, Length);
    }
    return status;
}

NTSTATUS
RequestGetHidXferPacketToRead(
    _In_ WDFREQUEST Request,
    _Out_ HID_XFER_PACKET* Packet
    )
{
    WDFMEMORY inputMemory;
    WDFMEMORY outputMemory;
    size_t inputLength;
    size_t outputLength;
    UCHAR* input;
    UCHAR* output;
    NTSTATUS status = WdfRequestRetrieveInputMemory(Request, &inputMemory);

    if (!NT_SUCCESS(status)) {
        return status;
    }

    input = (UCHAR*)WdfMemoryGetBuffer(inputMemory, &inputLength);
    if (input == NULL || inputLength < sizeof(UCHAR)) {
        return STATUS_INVALID_BUFFER_SIZE;
    }

    status = WdfRequestRetrieveOutputMemory(Request, &outputMemory);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    output = (UCHAR*)WdfMemoryGetBuffer(outputMemory, &outputLength);
    Packet->reportId = input[0];
    Packet->reportBuffer = output;
    Packet->reportBufferLen = (ULONG)outputLength;
    return STATUS_SUCCESS;
}

NTSTATUS
RequestGetHidXferPacketToWrite(
    _In_ WDFREQUEST Request,
    _Out_ HID_XFER_PACKET* Packet
    )
{
    WDFMEMORY inputMemory;
    WDFMEMORY outputMemory;
    size_t inputLength;
    size_t outputLength;
    UCHAR* input;
    NTSTATUS status = WdfRequestRetrieveOutputMemory(Request, &outputMemory);

    if (!NT_SUCCESS(status)) {
        return status;
    }

    WdfMemoryGetBuffer(outputMemory, &outputLength);
    status = WdfRequestRetrieveInputMemory(Request, &inputMemory);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    input = (UCHAR*)WdfMemoryGetBuffer(inputMemory, &inputLength);
    Packet->reportId = (UCHAR)outputLength;
    Packet->reportBuffer = input;
    Packet->reportBufferLen = (ULONG)inputLength;
    return STATUS_SUCCESS;
}
