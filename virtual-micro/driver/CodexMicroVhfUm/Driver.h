#pragma once

#define WIN32_NO_STATUS
#include <windows.h>
#undef WIN32_NO_STATUS
#include <ntstatus.h>
#include <wdf.h>
#include <vhf.h>

#include "Public.h"

#define VMICRO_OUTPUT_QUEUE_CAPACITY 64U

typedef struct _DEVICE_CONTEXT {
    VHFHANDLE VhfHandle;
    VHFHANDLE KeyboardVhfHandle;
    WDFIOTARGET VhfIoTarget;
    WDFIOTARGET KeyboardVhfIoTarget;
    SRWLOCK OutputLock;
    volatile LONG Stopping;

    UINT64 ConnectionEpoch;
    UINT64 LastBatchSequence;
    ULONG LastBatchReportCount;
    ULONG LastBatchDisposition;
    ULONG LastBatchAcceptedReports;
    NTSTATUS LastBatchStatus;

    UINT64 OutputSequence;
    ULONG DroppedOutputReports;
    ULONG OutputHead;
    ULONG OutputTail;
    ULONG OutputCount;
    VMICRO_OUTPUT_RECORD OutputQueue[VMICRO_OUTPUT_QUEUE_CAPACITY];
} DEVICE_CONTEXT, *PDEVICE_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(DEVICE_CONTEXT, DeviceGetContext)

DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD VmicroEvtDeviceAdd;
EVT_WDF_DEVICE_PREPARE_HARDWARE VmicroEvtDevicePrepareHardware;
EVT_WDF_DEVICE_RELEASE_HARDWARE VmicroEvtDeviceReleaseHardware;
EVT_WDF_OBJECT_CONTEXT_CLEANUP VmicroEvtDeviceCleanup;
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL VmicroEvtIoDeviceControl;
EVT_VHF_ASYNC_OPERATION VmicroEvtVhfWriteReport;
