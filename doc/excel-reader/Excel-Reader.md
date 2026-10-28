---
id: ctproduction-agents-excel-reader
title: "Excel-Reader: sync of maintenance orders from an Excel file"
subject: "Target Excel-Reader of the Workplace.Agents project: reads an Excel file with the FileReader and syncs its orders into ctProduction maintenance jobs"
keywords: [ctProduction, Workplace.Agents, Excel-Reader, FileReader, xlsx, MaintenanceJob, maintenance order, sync, pipeline, skill, HOS.Simulators]
---

# Excel-Reader

The target **`Excel-Reader`** of the project `src/Workplace.Agents/Workplace.Agents.h2proj` reads an Excel file with the HumanOS **FileReader** (`xlsx:` addresses) and a script turns the rows into **maintenance jobs in ctProduction**. A row is an operation of a maintenance order. The sync creates jobs for new rows, updates jobs that are still in the file and cancels jobs whose row disappeared.

> **The sync does not run when the file is saved.** Start it with **Run** on the task `Task.MaintenanceJob.ExcelSync` in the task list, or call the REST action `sync` on `MaintenanceJob`.

The jobs are created for the workplace **`Milling Center X`** of the target `HOS.Simulators`, see [Workplace and equipment](#workplace-and-equipment). The configuration that the platform needs (field, action, pipeline template, task) is delivered as import files in [`ctProduction/`](./ctProduction/Readme.md).

## Architecture

```text
MaintenanceOrders.xlsx  (src/Workplace.Agents, edited by hand, e.g. in Excel)
        │  FileReader, address xlsx:$.Tabelle1  (read only)
        ▼
gateway of the target Excel-Reader ── device DemoExcelReader ── data node SheetData (whole sheet as JSON array)
        │
Task.MaintenanceJob.ExcelSync (Run)  ──►  ctProduction action "sync" on MaintenanceJob
                                                  │
                                                  ▼
                                   pipeline Pipeline.MaintenanceSync.Sync
        │
        ▼
script Platform.MaintenanceSync.cs  (skill MaintenanceSync, agent Actor of the device)
        │  compares the sheet with the existing MaintenanceJob records
        ▼
ctProduction: create / update / cancel maintenance jobs  (workplace of the facility component with the InventoryNumber of the row)
```

## Prerequisites

- A licensed HumanOS IoT Runtime with Excel support in the FileReader: version **2.12.0.6863 or newer** (the target uses `HumanOS.IoT.Runtime win-x86` 2.12). A trial runtime reads the Excel file but cannot join the platform (it logs "Shared nodespace is not supported") and expires one hour after it was started
- HumanOS IoT Designer to build the targets `Excel-Reader` and `HOS.Simulators`
- A ctProduction **test** tenant, a user with the rights to run the `sync` action, and the configuration of the folder [`ctProduction/`](./ctProduction/Readme.md) applied to it. **Use a test tenant. The sync cancels every job that has an order number and an operation, is not started and whose row is not in the Excel file — regardless of who created it. In a shared tenant this will cancel jobs created by other users or processes.**
- The gateway of the target `HOS.Simulators` running, so that the platform knows the workplace `Milling Center X`
- Microsoft Excel to change the demo file

## Quick Start

1. Start a ctProduction test platform and note the code of your tenant.
2. Import the five files of [`ctProduction/`](./ctProduction/Readme.md) in the order of their numbers.
3. Open `Workplace.Agents.h2proj` in the Designer. Check that the targets `HOS.Simulators` and `Excel-Reader` carry the shared node space settings and the `ServiceId` of your platform (see [Build and Run](#build-and-run)), then build both.
4. Start both gateways and wait for `Successfully connected to the master controller` and `Actor 'Actor' registered` in the log of the Excel-Reader gateway.
5. In the platform open the list `Task`, select `Task.MaintenanceJob.ExcelSync` and press `Run`.
6. Check the result: 40 jobs at the workplace `Milling Center X`, see [Check the result](#check-the-result).
7. Change `MaintenanceOrders.xlsx`, save it, press `Run` again: the steps of the [Demo](#demo).

## Key Concepts

- **Manual trigger.** The sync runs only when it is started explicitly: with the button **Run** on the task `Task.MaintenanceJob.ExcelSync` in the ctProduction task list, or by calling the REST action `sync` on `MaintenanceJob`. Saving the Excel file updates the data node on the gateway; the jobs in ctProduction are not changed until you start the sync.
- **One fixed file.** The FileReader watches exactly one file name (`MaintenanceOrders.xlsx`, next to `Workplace.Agents.h2proj`). The file must exist when the gateway starts. It does not watch a folder for new or dated files.
- **Read only.** Excel addresses are read-only; the gateway never changes the workbook.
- **The whole sheet as one value.** The address `xlsx:$.Tabelle1` returns every row of the sheet `Tabelle1` as a JSON array. The first row holds the column names, which become the keys. A repeated column name gets the suffix `_2` (the file has two columns `Bezeichnung`). Numbers and dates arrive as text, dates in ISO 8601.
- **Relative path.** The device address `..\..\..\MaintenanceOrders.xlsx` is resolved against the data folder of the built gateway (`Build\Excel-Reader\Data`), which leads back to the folder of `Workplace.Agents.h2proj`. This holds for a gateway started from the built folder; for a gateway installed somewhere else set the device address to the absolute path of the Excel file.
- **Identity of a job.** `Auftrag` (order) + `Vorgang` (operation) form the key, stored as `OrderNumber` + `Operation`. The columns `Auftrag`, `Personalnr`, `Vorgang` and `Equipment` are formatted as text: keep them text, because a number typed into a general cell loses its leading zeros (`0010` becomes `10`, which is another operation).
- **Soft delete.** A job whose row is gone gets `IsCancelled = true`, but only while it is not started (state below 300: not ready or ready); nothing is deleted. A job that was already started (setup, running or paused) is left alone and the pipeline log names it, so a typo in `Auftrag` or `Vorgang` cannot cancel a job that runs at the workplace. A cancelled or finished job is final: it is never reactivated, and a row for it is ignored.
- **Which jobs the sync manages.** Jobs that belong to a maintenance task (generated from the maintenance schedule) are never touched. **Every other job that has an order number and an operation is managed by the sync: if its row is not in the Excel file and it is not started, the job is cancelled — regardless of who created it.** In a shared tenant this cancels jobs from other sources. Use a test tenant, or add a field `Source = "Excel"` to the model and restrict the sync query to that value so it manages only the jobs it created itself.
- **Equipment must be known.** A new job needs a workplace. The column `Equipment` must equal the `InventoryNumber` of a `FacilityComponent` that belongs to a workplace; otherwise the row is dropped and logged.
- **Empty sheet.** A sheet without a usable row stops the sync with an error instead of cancelling every job.

## Workplace and equipment

The workbook `MaintenanceOrders.xlsx` has `A-18221` in the column `Equipment` of every row. This is the `MachineInventoryNumber` of the robot (`Robot`) of the workplace **`Milling Center X`** of the target `HOS.Simulators`, the only facility component of the simulators that has an inventory number. The sync finds the facility component by this number and creates every job for its workplace. Because the workplace has a workplace agent (the gateway of `HOS.Simulators`), a job can be started and finished in ctProduction.

To use another workplace, give one of its facility components an `InventoryNumber` (property `MachineInventoryNumber` of its device) and use that number in the column `Equipment`. A row with an `Equipment` that no facility component has is dropped and logged; this is what step 7 of the demo shows.

## Project contents

| Path (below `src/Workplace.Agents`)                    | Purpose                                                          |
| :----------------------------------------------------- | :--------------------------------------------------------------- |
| `Workplace.Agents.h2proj`                              | Designer project; the target `Excel-Reader` and its device template are entries of it |
| `MaintenanceOrders.xlsx`                               | Demo data: 40 operations of 8 orders, sheet `Tabelle1`, `Equipment` `A-18221` |
| `Excel-Reader/Devices/DemoExcelReader.json`            | Device `DemoExcelReader` of the target, created from the template |
| `Excel-Reader/Scripts/Workflows/Platform.MaintenanceSync.cs` | The sync script run by the ctProduction pipeline         |
| `DeviceTemplates/Excel-Reader.json`                    | Device template with the data node `SheetData`, the agent `Actor` and the skill `MaintenanceSync`; the device inherits them |
| `globalids.json`                                       | Ids of the nodes of the device (`.../Excel-Reader/SheetData`, `/Actor`, `/MaintenanceSync`) |

The import files for the platform are in `doc/excel-reader/ctProduction`, see their [Readme](./ctProduction/Readme.md).

The Excel columns are: `Auftrag`, `Auftrag Bezeichnung`, `Kurztext`, `Personalnr`, `Istarbeit`, `Einheit Arbeit`, `VrgArbeitsplatz`, `Auftragsart`, `Erfassungsdatum`, `Start Uhrzeit`, `Vorgang`, `Langtext vorh.`, `Priorität`, `Techn. Platz`, `Bezeichnung`, `Equipment`, `Bezeichnung`, `Kostenstelle`, `Erfasser`. The sync uses `Auftrag`, `Auftrag Bezeichnung`, `Kurztext`, `Vorgang`, `Equipment`, `Erfassungsdatum`, `Istarbeit` and `Einheit Arbeit`; keep the headers and keep `Erfassungsdatum` a date. A job gets these values:

| Job field                      | From                                                                                   |
| :----------------------------- | :------------------------------------------------------------------------------------- |
| `Name`                         | `Auftrag/Vorgang Kurztext`, e.g. `9100001/0010 Visual inspection` (the order description if `Kurztext` is empty) |
| `OrderDescription`             | `Auftrag Bezeichnung`                                                                  |
| `EntryDate`, `SetStartDate`    | `Erfassungsdatum` (start date)                                                         |
| `DueDate`                      | Last day of the month of `Erfassungsdatum`, 23:59:59                                   |
| `SetDuration`                  | `Istarbeit` in the unit of `Einheit Arbeit` (`H`, `MIN` or `S`; no unit means hours). The file has no planned work, so the work of the row is used. Not written when empty or not a number |
| `FacilityComponentId`          | the facility component whose `InventoryNumber` equals `Equipment` (required field of the job) |
| `WorkplaceId`                  | the workplace of that facility component                                               |

A date typed into a cell that is not formatted as date arrives as a number; the sync converts it. A text that is not a date is not written (the job is still created or updated) and the pipeline log names the order.

## Build and Run

1. Import the ctProduction configuration ([ctProduction/Readme.md](./ctProduction/Readme.md)).
2. Connect the targets to the platform. The platform and the gateways talk over a shared node space (MQTT). The targets `default`, `HOS.Simulators` and `Excel-Reader` of this project carry the shared node space type, the address `localhost` and the `ServiceId` of the **development platform** as master system controller and master agent orchestrator (both the same value). If your platform is another one, set the **Shared Node Space** and the `ServiceId` of **your** platform in the Designer, target tab `HumanOS Kernel`. It is the `ServiceId` of the platform web server (`HOS.WebServer`); the platform logs it at start (`ServiceId : {...}`). The gateway log shows `No shared nodespace configured.` as long as this is not set.
3. Build the targets `HOS.Simulators` and `Excel-Reader` in the Designer. A target with a licensed runtime builds only in the Designer.
4. Start both gateways. They register themselves and their actors at the platform; nothing has to be published. For a local test the gateway (`HumanOS.IoT.Gateway.exe` in the folder of the installed runtime) can be started directly with the built folder:

   ```text
   HumanOS.IoT.Gateway.exe -AppSettingsDir="<project folder>\Build\Excel-Reader"
   ```

   About five seconds after the start the Excel gateway has read the Excel file (the log shows `File watcher started on file ...MaintenanceOrders.xlsx`; the line `Excel content model successfully updated the content` appears only with the log level `Verbose`, set `AppConfig.LogMinimumLevel` in the target). With the platform connection the log also shows `Successfully connected to the master controller` and `Actor 'Actor' registered`.

## Demo

All steps change `MaintenanceOrders.xlsx` (save the file, you can keep it open in Excel) and then start the sync again. The gateway picks up a saved file within a second; wait a moment after saving before you start the sync.

Start the sync in the portal: select the task `Task.MaintenanceJob.ExcelSync` in the task list and press `Run`. The same can be done with a call to the ctProduction REST interface. It answers with the id of the started pipeline:

```text
POST https://<ctProduction host>/api/v2/<tenant code>/entities/executeaction
Authorization: Bearer <token>
Content-Type: application/json

{ "ActionName": "sync", "CollectionId": "b42394d1-130a-485c-8a7e-7bfd82396a08" }
```

The pipeline needs the gateway to answer for its skill. The platform asks every registered agent in turn, so an agent that is registered but not running makes the sync fail with `hasSkill failed: Timeout` (start all agents of the platform, or restart the platform to clear the stale registrations).

| Step | What you change in the Excel file                                                  | What the sync does                                                       |
| :--- | :--------------------------------------------------------------------------------- | :----------------------------------------------------------------------- |
| 1    | Nothing, run the sync                                                              | Creates the 40 jobs (8 orders, 5 operations each)                        |
| 2    | Change `Erfassungsdatum` of order `9100001`, operation `0010`                      | Updates start date and due date (end of the month) of that job           |
| 3    | Change `Kurztext` (and/or `Auftrag Bezeichnung`) in one row of order `9100001`     | Updates the name of that job (`Kurztext`, visible in the list) and its field `OrderDescription` (`Auftrag Bezeichnung`) |
| 4    | Add a row: order `9100009`, operation `0010`, equipment `A-18221`, a date          | Creates a new job                                                        |
| 5    | Delete a row                                                                       | Cancels that job (`IsCancelled`) if it is not started, it is not deleted |
| 6    | Add the deleted row again                                                          | The job stays cancelled and the row is ignored. Use a new order number to get a new job |
| 7    | Add a row with an `Equipment` that no facility component has                       | The row is dropped, the pipeline log names the reason                    |
| 8    | Start a job of the sync in the portal and finish it, then change that row and run the sync | The finished job (state 900) stays unchanged; the gateway log says the row is ignored |

The result is visible in the list of maintenance jobs of ctProduction (new jobs with name, start date and due date, cancelled jobs). Every sync writes name, entry date and description of all jobs that are still in the file again, whether they changed or not; start date, due date and duration are written only for jobs that are not started. A changed `Equipment` does not move a job to another workplace (the facility component of a job cannot be changed after create); the pipeline log names such a job.

## Check the result

**Logs.** After a sync the gateway log shows `OPERATION for Job 'SyncJob'`, `Trigger: sync`, `Maintenance sync: N MaintenanceJob change(s) from M Excel row(s).` and `Job successfully finished.` Rows that are skipped or dropped (unknown equipment, no date, duplicates, final jobs) are named in the log. The platform log shows the activity `SyncJob` assigned to the actor `Actor`.

**Jobs.** After step 1 the list of maintenance jobs holds these values (the details view shows `Name`, the planning fields and the order number; `OrderDescription` is a field of the job that the standard views do not show):

| Check               | Expected                                                                                   |
| :------------------ | :----------------------------------------------------------------------------------------- |
| Count               | 40 jobs, 8 orders, operations `0010` to `0050`, none cancelled                             |
| `Name`              | `<Auftrag>/<Vorgang> <Kurztext>`, e.g. `9100001/0010 Visual inspection`                    |
| `SetStartDate`      | `Erfassungsdatum` of the row                                                               |
| `DueDate`           | last day of that month, 23:59:59                                                           |
| `SetDuration`       | `Istarbeit` in hours: eight jobs each with 00:30, 00:45, 01:00, 01:30 and 02:00             |
| `WorkplaceId`       | the workplace `Milling Center X`                                                           |

The same list can be read through the REST interface (read only; the answer holds `entities`, the job fields are in `fields`):

```powershell
# only for a development platform with a self-signed certificate:
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
$Hdr  = @{ Authorization = "Bearer $env:HOS_TOKEN" }
$Body = '{"CollectionId":"b42394d1-130a-485c-8a7e-7bfd82396a08","Count":200,"Offset":0}'
$Jobs = (Invoke-RestMethod -Method Post -Uri "https://<platform host>/api/v2/<tenant code>/entities/queryentitylist" -Headers $Hdr -ContentType 'application/json' -Body $Body).entities | ForEach-Object { $_.fields }
$Jobs | Sort-Object OrderNumber, Operation | Select-Object Name, SetStartDate, DueDate, SetDuration, IsCancelled | Format-Table -AutoSize
```

## Troubleshooting

| Symptom                                                         | Cause and remedy                                                                                              |
| :-------------------------------------------------------------- | :------------------------------------------------------------------------------------------------------------ |
| Gateway log `No shared nodespace configured.`                   | Shared node space or `ServiceId` of the platform is not set in the target. With a trial runtime the log says `Shared nodespace is not supported`: use a licensed runtime |
| Sync fails with `hasSkill failed: Timeout`                      | The platform asks every registered agent in turn; an agent that is registered but not running breaks it. Start all agents of the platform (including the gateway of `HOS.Simulators`) or restart the platform |
| Sync fails with `No actor matching skill '...' found`           | The skill id of the agent does not match. The id `9cf292c6-e00b-417f-84e3-00ff754f9ab8` must be the same in the skill, in the `Skills` list and in the property `Skills` of the agent (the Designer can rewrite these references when it saves the project) and in `SkillId` of the pipeline template (file 04) |
| Sync fails with `Data node 'SheetData' not found on device '<id>'` | The script looks up the data node on the device with the id in the constant `m_ExcelReaderDeviceId` (end of `Platform.MaintenanceSync.cs`). It must be the id of the device `DemoExcelReader` (`dc89beab-c280-43af-a626-99681beefffc`). Change it when you create the device again or rename it, build the target and restart the gateway |
| Every row is dropped with `no usable FacilityComponent`         | The platform has no facility component with the `InventoryNumber` of the column `Equipment`, or the component has no workplace. Start the gateway of `HOS.Simulators`, check that the robot of `Milling Center X` has `A-18221` and belongs to the workplace |
| Starting a job fails with `No actor matching skill '{<workplace id>}'` | Not part of the sync. A job is started by a workplace agent that serves the workplace: start the gateway of `HOS.Simulators` |
| Import answers `Data does not match the selected entity model`  | The file is imported into the wrong list; the `CollectionId` of the file must be the collection of the list    |
| `ActionConfiguration` is not among the fields of `MaintenanceJob` | It is a property of the model, not a field. Import file 03 as described in the [Readme of `ctProduction/`](./ctProduction/Readme.md) |
| Task `Task.MaintenanceJob.ExcelSync` is not in the task list    | Only active tasks are listed. File 05 creates it active; import the current file again                        |
| A job has no dates                                              | `Erfassungsdatum` is not a date. The sync converts Excel numbers, anything else is logged and not written     |
| `Maintenance sync` is not in the log, an old script runs        | The gateway compiles the script when it starts. Build the target again and restart the gateway                |
| Sync stops with `no usable data rows`                           | The sheet is empty or holds only malformed rows. This is intended: it keeps a half-written file from cancelling every job |
| The gateway does not read the Excel file                        | The device of the target must use the driver of the FileReader and the file must exist next to `Workplace.Agents.h2proj` (path `..\..\..\MaintenanceOrders.xlsx` from the data folder of the build). The log shows `File watcher started on file ...` when it works |

## Limits

- One fixed file name, existing at gateway start. A file that is saved under a new name is not noticed.
- The sync runs when the `sync` action is called (REST, the task `Run` button or a ctProduction script); it does not run by itself unless you give the task a shorter trigger.
- The gateway compiles the sync script when it starts; restart the gateway after you change the script.
- The `sync` action and its pipeline are delivered as import files in `doc/excel-reader/ctProduction` and are not part of the standard ctProduction model yet. Import them into a test tenant first.
