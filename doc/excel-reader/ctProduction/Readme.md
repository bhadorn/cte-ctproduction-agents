---
id: ctproduction-agents-excel-reader-import
title: "Excel-Reader: ctProduction configuration"
subject: "Configuration to import into ctProduction for the Excel-Reader sync of maintenance jobs"
keywords: [ctProduction, MaintenanceJob, sync action, pipeline template, Excel-Reader, Workplace.Agents, import]
---

# ctProduction configuration for the Excel-Reader

The numbered files in this folder are **import files** in the entity export format of the platform. Import them in the order of their numbers into a running platform with ctProduction. They add the sync to the existing `MaintenanceJob` model; nothing else of your model is replaced. Use a **test tenant** first. The target that feeds the sync is the target `Excel-Reader` of `src/Workplace.Agents`, described in [Excel-Reader.md](../Excel-Reader.md).

| File                                         | What it imports                                                                                                              |
| :------------------------------------------- | :--------------------------------------------------------------------------------------------------------------------------- |
| `01.MaintenanceJob.OrderDescription.field.json` | Field `OrderDescription` (text) on `MaintenanceJob`, filled from the Excel column `Auftrag Bezeichnung`; not shown in the standard views, add it to the details view of `MaintenanceJob` if you want to see it |
| `02.MaintenanceJob.sync.action.json`         | Model action `sync` on `MaintenanceJob` (no selection needed, policy `POLICY_Supervisor`)                                    |
| `03.MaintenanceJob.ActionConfiguration.json` | The `ActionConfiguration` of `MaintenanceJob`: the standard entries plus `sync`, which runs the pipeline template of file 04 |
| `04.Pipeline.MaintenanceSync.Sync.json`      | Pipeline template `Pipeline.MaintenanceSync.Sync`: one job that runs the script `Platform.MaintenanceSync.cs` on the skill `MaintenanceSync` of the gateway |
| `05.Task.MaintenanceJob.ExcelSync.json`      | Task `Task.MaintenanceJob.ExcelSync`: runs the action `sync` of `MaintenanceJob`. Start it with the button `Run` in the task list |

There is no demo data to import: the jobs are created for the workplace `Milling Center X` of the target `HOS.Simulators`, see [Workplace and equipment](#workplace-and-equipment).

## Import

Import every file with the import function of the platform (you need the role System Admin). Each list of the portal has an `Import` action in its menu; open the list and pick the file:

| File | List to open                                                                                                         |
| :--- | :------------------------------------------------------------------------------------------------------------------- |
| 01   | `ModelField`, address `/ModelField/ListView` (or the model `MaintenanceJob`, tab Fields)                             |
| 02   | `ModelAction`, address `/ModelAction/ListView` (or the model `MaintenanceJob`, tab Actions)                          |
| 03   | `Model`, address `/Model/ListView`. The file updates one property of the model `MaintenanceJob`; the property `ActionConfiguration` is not shown among its fields |
| 04   | `PipelineTemplate`                                                                                                   |
| 05   | `Task`                                                                                                               |

Every list answers with a success message. The same files can be sent to the REST interface; this was tried against a running platform with the current ctProduction model:

```powershell
# only for a development platform with a self-signed certificate:
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
$Base = 'https://<platform host>/api/v2/<tenant code>/entities'
$Hdr  = @{ Authorization = "Bearer $env:HOS_TOKEN" }   # a token of a user who may import configuration
# run from the folder doc/excel-reader
Get-ChildItem .\ctProduction\0?.*.json | Sort-Object Name | ForEach-Object {
  Invoke-RestMethod -Method Post -Uri "$Base/directimport" -Headers $Hdr `
    -ContentType 'application/json; charset=utf-8' -Body ([IO.File]::ReadAllBytes($_.FullName))
}
```

Each call answers with `"state": "Succeeded"`. The pipeline template `04` passes `MaintenanceTaskId` to the sync, which is how the sync recognises jobs generated from the maintenance schedule (it never touches them). Importing a file again changes nothing (the entities have fixed ids), so a failed run can simply be repeated.

File 03 contains the whole `ActionConfiguration` of `MaintenanceJob` of the ctProduction version it was taken from (the standard entries plus `sync`). If you changed the action configuration of your model, add the `sync` entry to your own configuration instead of importing the file:

```json
"sync": { "ExecutionPolicy": "POLICY_Supervisor", "PipelineTemplateId": "594d2e5f-797c-473f-a1a6-fe6a56c6bf60" }
```

Check after the import that the pipeline template refers to the skill `MaintenanceSync` of the gateway (`SkillId` in file 04 equals the id of the skill in `src/Workplace.Agents/Excel-Reader/Devices/DemoExcelReader.json`) and that `OperationScriptName` is `Platform.MaintenanceSync.cs`. If you rename the script or generate new ids, change both sides.

## Workplace and equipment

The sync creates a job for a row of the Excel file only if the column `Equipment` equals the `InventoryNumber` of a facility component that belongs to a workplace. The workbook `src/Workplace.Agents/MaintenanceOrders.xlsx` uses `A-18221`, the robot of the workplace `Milling Center X` of the target `HOS.Simulators`. Start the gateway of that target (and every other gateway of the platform) before the first sync, so that the platform knows the workplace and its components. To use another workplace, give one of its facility components an `InventoryNumber` and use it in the column `Equipment`.

## Check the import

After the five imports the platform holds: the field `OrderDescription` and the action `sync` on `MaintenanceJob`, the entry `sync` in its `ActionConfiguration`, the pipeline template `Pipeline.MaintenanceSync.Sync` and the active task `Task.MaintenanceJob.ExcelSync`. If a list stays empty, look at the troubleshooting table of [Excel-Reader.md](../Excel-Reader.md#troubleshooting).

## Start the sync from the portal

File 05 is a task (list `Task`, import it there like the others). Select the task `Task.MaintenanceJob.ExcelSync` in the task list and press `Run` (the play button) whenever you want the sync to run. The trigger of the task is set ten years ahead, so it never starts by itself. Change the trigger to a shorter interval if you want a regular sync.

The task runs in the context of the system (empty user and tenant), like the standard tasks of ctProduction. A task that is not active does not appear in the list, so the file creates it active.

## Fixed ids

The collection id `b42394d1-130a-485c-8a7e-7bfd82396a08` (`MaintenanceJob`) and `6ccc7179-9125-4b4b-baa4-9849351c1187` (`FacilityComponent`) are model ids of ctProduction and are used by the script unchanged. The ids of the device, node, skill, agent and the new field belong to this example only. The ones you meet while testing: skill `MaintenanceSync` `9cf292c6-e00b-417f-84e3-00ff754f9ab8`, pipeline template `Pipeline.MaintenanceSync.Sync` `594d2e5f-797c-473f-a1a6-fe6a56c6bf60`, task `Task.MaintenanceJob.ExcelSync` `e2ca0602-8916-45b4-8a1f-daf74e681c45`.
