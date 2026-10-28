/*****************************************************************************
 * Copyright (C) by CyberTech Engineering 2026 – www.cybertech.swiss         *
 *****************************************************************************
 * Project: HumanOS (R)
 * Date   : 2026
 *****************************************************************************
 * License:                                                                  *
 *   This library is protected software; you are not allowed to redistribute *
 *   whole or part of it to other companies or external persons without the  *
 *   authorization of the CEO CyberTech Engineering GmbH.                    *
 *****************************************************************************/

using HumanOS.Kernel;
using HumanOS.Kernel.Utils;
using HumanOS.Kernel.Workflow.Activity;
using HumanOS.PeMiL.PlatformBinding.JsonModels;
using HumanOS.PeMiL.PlatformBinding.Pipelines;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using HumanOS.Kernel.DataModel;
using HumanOS.Kernel.DataModel.Space;

namespace HumanOS.IoT.Designer.Library.Scripts
{
  /// <summary>
  /// Reconciles a maintenance-order Excel sheet against the platform's known MaintenanceJob records.
  /// Triggered on the MaintenanceJob "sync" action.
  /// </summary>
  public class TMaintenanceSyncOperation : TAbstractPipelineOperationScriptObject
  {
    #region Constants

    private const string SheetDataNodeName = "SheetData";

    private const int JobStateDone = 900;

    // Job states below JobState_Setup (300) are not started: 0 is not ready, 200 is ready (start date reached).
    private const int JobStateSetup = 300;

    #endregion Constants

    #region Implementation of TAbstractPipelineOperationScriptObject

    ///<see cref="TAbstractPipelineOperationScriptObject"/>
    protected override async Task<TPipelineOutputJM> runJobAsync(IKernelAccess Kernel,
                                                                 IActivity Activity,
                                                                 TPipelineExecutionContext PipelineContext,
                                                                 CancellationToken Token)
    {
      TPipelineOutputJM Retval = new TPipelineOutputJM();

      try
      {
        if (PipelineContext.TriggerAction != "sync")
        {
          throw new ArgumentException($"Invalid trigger action '{PipelineContext.TriggerAction}'.");
        }

        List<TEntityDataJM> lstInventory = JsonConvert.DeserializeObject<List<TEntityDataJM>>(PipelineContext.DataInput);
        List<TEntityDataJM> lstKnownJobs = lstInventory.Where(Entity => Entity.CollectionId == m_MaintenanceJobCollectionId).ToList();
        List<TEntityDataJM> lstComponents = lstInventory.Where(Entity => Entity.CollectionId == m_FacilityComponentCollectionId).ToList();

        JArray jExcelRows = await readExcelRowsAsync(Kernel, Token).ConfigureAwait(false);

        List<string> lstLog = new List<string>();
        Retval.Data.AddRange(computeDiff(lstKnownJobs, lstComponents, jExcelRows, lstLog));
        foreach (string strLogLine in lstLog)
        {
          writeError(strLogLine);
        }
        writeInfo($"Maintenance sync: {Retval.Data.Count} MaintenanceJob change(s) from {jExcelRows.Count} Excel row(s).");
      }
      catch (Exception Exc) when (Exc.isNotCancelException())
      {
        writeError($"{Exc.Message}\n{Exc.StackTrace}");
        throw;
      }
      return await Task.FromResult(Retval).ConfigureAwait(false);
    }

    #endregion Implementation of TAbstractPipelineOperationScriptObject

    // Name shown in the job list: "<order>/<operation> <short text of the operation>"; without a short text the order description follows.
    internal static string buildJobName(string strOrder, string strOperation, string strShortText, string strDescription)
    {
      string strDetail = strShortText.Length > 0 ? strShortText : strDescription;
      return strDetail.Length > 0 ? $"{strOrder}/{strOperation} {strDetail}" : $"{strOrder}/{strOperation}";
    }

    // Computes the three-way diff between the platform's known MaintenanceJob inventory and the
    // Excel sheet, keyed by OrderNumber + Operation (Excel columns Auftrag + Vorgang).
    //  - Only jobs with an order number and an operation take part, and only jobs that do not belong to a
    //    maintenance task (MaintenanceTaskId empty): the jobs generated from the maintenance schedule are
    //    never touched. Jobs created elsewhere (e.g. by the
    //    maintenance schedule or by hand) are never touched.
    //  - Jobs that are done or cancelled are final: a matching Excel row is ignored (logged), a missing one
    //    does not cancel them again, and a cancelled job is never reactivated.
    //  - In Excel and platform -> patch Name, OrderDescription and, when Erfassungsdatum is a date, EntryDate. A job that is
    //    not started (State below 300) also gets SetStartDate (the date), DueDate (end of that month) and SetDuration from Istarbeit
    //    when it is a usable number; the planning of a started job is left alone. FacilityComponentId cannot be changed
    //    after create, so an Equipment that now belongs to another component is only logged.
    //  - In Excel only -> create (with FacilityComponentId and WorkplaceId of the component), but only if the equipment
    //    resolves to a FacilityComponent with a workplace.
    //  - On the platform only -> soft-delete via IsCancelled, but only while the job is not started (State below 300);
    //    a job that was started or paused is left alone and logged.
    // Problems are appended to lstLog. A sheet without a single usable row throws, so an empty or half-written
    // file can never cancel every job.
    internal List<TEntityDataJM> computeDiff(List<TEntityDataJM> lstKnownJobs, List<TEntityDataJM> lstComponents, JArray jExcelRows, List<string> lstLog)
    {
      List<TEntityDataJM> Retval = new List<TEntityDataJM>();

      Dictionary<string, TEntityDataJM> dicActiveByKey = new Dictionary<string, TEntityDataJM>();
      Dictionary<string, string> dicFinalStateByKey = new Dictionary<string, string>();
      foreach (TEntityDataJM Job in lstKnownJobs)
      {
        string strOrderNumber = Job.readFieldStringValue("OrderNumber", "").Trim();
        string strOperation = Job.readFieldStringValue("Operation", "").Trim();
        if (strOrderNumber.Length > 0 && strOperation.Length > 0)
        {
          string strKey = getExternalKey(strOrderNumber, strOperation);
          bool bDone = Job.readField<int>("State", 0) == JobStateDone;
          bool bCancelled = Job.readField<bool>("IsCancelled", false);
          bool bOfMaintenanceTask = Guid.TryParse(Job.readFieldStringValue("MaintenanceTaskId", ""), out Guid MaintenanceTaskId) && MaintenanceTaskId != Guid.Empty;
          if (bOfMaintenanceTask || bDone || bCancelled)
          {
            dicFinalStateByKey.TryAdd(strKey, bOfMaintenanceTask ? "of a maintenance task" : bDone ? "done" : "cancelled");
          }
          else if (!dicActiveByKey.TryAdd(strKey, Job))
          {
            lstLog.Add($"Duplicate active job for {strKey}: extra job {Job.Id} ignored.");
          }
        }
      }

      Dictionary<string, Guid> dicComponentByEquipment = new Dictionary<string, Guid>();
      Dictionary<string, Guid> dicWorkplaceByEquipment = new Dictionary<string, Guid>();
      foreach (TEntityDataJM Component in lstComponents)
      {
        string strInventoryNumber = Component.readFieldStringValue("InventoryNumber", "").Trim();
        if (strInventoryNumber.Length > 0)
        {
          if (Guid.TryParse(Component.readFieldStringValue("WorkplaceId", ""), out Guid WorkplaceId) && WorkplaceId != Guid.Empty)
          {
            if (dicWorkplaceByEquipment.TryAdd(strInventoryNumber, WorkplaceId))
            {
              dicComponentByEquipment.Add(strInventoryNumber, Component.Id);
            }
            else
            {
              lstLog.Add($"Duplicate InventoryNumber '{strInventoryNumber}': first component kept.");
            }
          }
          else
          {
            lstLog.Add($"FacilityComponent {Component.Id} ('{strInventoryNumber}') has no valid WorkplaceId and is ignored.");
          }
        }
      }

      HashSet<string> setExcelKeys = new HashSet<string>();
      int iValidRows = 0;
      foreach (JObject jRow in jExcelRows.Children<JObject>())
      {
        string strAuftrag = readText(jRow, "Auftrag");
        string strVorgang = readText(jRow, "Vorgang");

        if (strAuftrag.Length == 0 || strVorgang.Length == 0)
        {
          lstLog.Add($"Skipped row without Auftrag/Vorgang: {jRow.ToString(Formatting.None)}");
        }
        else
        {
          iValidRows++;
          string strKey = getExternalKey(strAuftrag, strVorgang);
          if (!setExcelKeys.Add(strKey))
          {
            lstLog.Add($"Duplicate Excel row for {strKey} ignored.");
          }
          else
          {
            string strEntryDate = normalizeEntryDate(readText(jRow, "Erfassungsdatum"), strKey, lstLog);
            string strDueDate = getEndOfMonth(strEntryDate);
            string strDescription = readText(jRow, "Auftrag Bezeichnung");
            string strName = buildJobName(strAuftrag, strVorgang, readText(jRow, "Kurztext"), strDescription);
            string strDuration = parseDuration(readText(jRow, "Istarbeit"), readText(jRow, "Einheit Arbeit"), strKey, lstLog);
            string strEquipment = readText(jRow, "Equipment");

            if (dicActiveByKey.TryGetValue(strKey, out TEntityDataJM Known))
            {
              bool bNotStarted = Known.readField<int>("State", 0) < JobStateSetup;
              TEntityDataJM Update = new TEntityDataJM() { Id = Known.Id, CollectionId = m_MaintenanceJobCollectionId };
              Update.Fields["Name"] = strName;
              if (strEntryDate.Length > 0)
              {
                Update.Fields["EntryDate"] = strEntryDate;
                if (bNotStarted)
                {
                  Update.Fields["SetStartDate"] = strEntryDate;
                  Update.Fields["DueDate"] = strDueDate;
                }
              }
              Update.Fields["OrderDescription"] = strDescription;
              if (bNotStarted && strDuration.Length > 0)
              {
                Update.Fields["SetDuration"] = strDuration;
              }
              Retval.Add(Update);

              if (Guid.TryParse(Known.readFieldStringValue("FacilityComponentId", ""), out Guid KnownComponentId) &&
                  dicComponentByEquipment.TryGetValue(strEquipment, out Guid ExcelComponentId) &&
                  KnownComponentId != ExcelComponentId)
              {
                lstLog.Add($"Order {strKey}: Equipment '{strEquipment}' belongs to another facility component than job {Known.Id}; the job keeps its workplace.");
              }
            }
            else if (dicFinalStateByKey.TryGetValue(strKey, out string strFinalState))
            {
              lstLog.Add($"Order {strKey} is {strFinalState}; row ignored.");
            }
            else if (strEquipment.Length > 0 && dicWorkplaceByEquipment.TryGetValue(strEquipment, out Guid WorkplaceId))
            {
              TEntityDataJM Create = new TEntityDataJM() { Id = Guid.Empty, CollectionId = m_MaintenanceJobCollectionId };
              Create.Fields["Name"] = strName;
              Create.Fields["OrderNumber"] = strAuftrag;
              Create.Fields["Operation"] = strVorgang;
              Create.Fields["OrderDescription"] = strDescription;
              if (strEntryDate.Length > 0)
              {
                Create.Fields["EntryDate"] = strEntryDate;
                Create.Fields["SetStartDate"] = strEntryDate;
                Create.Fields["DueDate"] = strDueDate;
              }
              if (strDuration.Length > 0)
              {
                Create.Fields["SetDuration"] = strDuration;
              }
              Create.Fields["IsCancelled"] = false;
              Create.Fields["FacilityComponentId"] = dicComponentByEquipment[strEquipment];
              Create.Fields["WorkplaceId"] = WorkplaceId;
              Retval.Add(Create);
            }
            else
            {
              lstLog.Add($"Dropped order {strKey}: no usable FacilityComponent for equipment number '{strEquipment}'.");
            }
          }
        }
      }

      if (iValidRows == 0)
      {
        throw new InvalidOperationException("The Excel sheet contains no usable data rows. Sync aborted so that no job is cancelled by mistake." + (lstLog.Count > 0 ? " " + string.Join(" | ", lstLog.Take(5)) : ""));
      }

      foreach (KeyValuePair<string, TEntityDataJM> Entry in dicActiveByKey)
      {
        if (!setExcelKeys.Contains(Entry.Key))
        {
          int iState = Entry.Value.readField<int>("State", 0);
          if (iState < JobStateSetup)
          {
            TEntityDataJM Cancel = new TEntityDataJM() { Id = Entry.Value.Id, CollectionId = m_MaintenanceJobCollectionId };
            Cancel.Fields["IsCancelled"] = true;
            Retval.Add(Cancel);
          }
          else
          {
            lstLog.Add($"Order {Entry.Key} is no longer in the Excel sheet but already started (State {iState}); not cancelled.");
          }
        }
      }

      return Retval;
    }

    // The due date is the last day of the month of the (ISO text) date, at the end of that day, as ISO text without offset
    // like the date itself. An empty date gives an empty text.
    internal static string getEndOfMonth(string strIsoDate)
    {
      string strRetval = "";
      if (strIsoDate.Length > 0 &&
          DateTime.TryParse(strIsoDate, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime Date))
      {
        DateTime EndOfMonth = new DateTime(Date.Year, Date.Month, DateTime.DaysInMonth(Date.Year, Date.Month), 23, 59, 59);
        strRetval = EndOfMonth.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
      }
      return strRetval;
    }

    // Returns the date as ISO text, or an empty text (date not written) when the cell holds no date.
    // ISO text is passed unchanged, an Excel serial number (date typed into a cell without a date format) is converted,
    // anything else (e.g. a date in a local text format) is not guessed.
    internal static string normalizeEntryDate(string strRaw, string strKey, List<string> lstLog)
    {
      string strRetval = "";
      if (strRaw.Length > 0)
      {
        if (System.Text.RegularExpressions.Regex.IsMatch(strRaw, @"^\d{4}-\d{2}-\d{2}") &&
            DateTime.TryParse(strRaw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out _))
        {
          strRetval = strRaw;
        }
        else if (double.TryParse(strRaw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double fSerial) &&
                 fSerial >= 1 && fSerial < 2958466)
        {
          strRetval = DateTime.FromOADate(fSerial).ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
          lstLog.Add($"Order {strKey}: Erfassungsdatum '{strRaw}' is not a date, EntryDate not written.");
        }
      }
      return strRetval;
    }

    // The set duration of the job is the work of the row (Istarbeit) in the unit of Einheit Arbeit (H, MIN or S; no unit means hours),
    // returned as the TimeSpan text "[d.]hh:mm:ss". An empty work gives an empty text (duration not written); a value that
    // is not a non-negative number or a unit that is not known is not written and logged.
    internal static string parseDuration(string strWork, string strUnit, string strKey, List<string> lstLog)
    {
      string strRetval = "";
      if (strWork.Length > 0)
      {
        string strUpperUnit = strUnit.ToUpperInvariant();
        double fFactorSeconds = strUpperUnit == "H" || strUpperUnit.Length == 0 ? 3600.0 : strUpperUnit == "MIN" ? 60.0 : strUpperUnit == "S" ? 1.0 : 0.0;
        if (fFactorSeconds > 0.0 &&
            double.TryParse(strWork.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double fWork) &&
            fWork >= 0.0 && fWork < 100000.0)
        {
          strRetval = TimeSpan.FromSeconds(Math.Round(fWork * fFactorSeconds)).ToString("c", System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
          lstLog.Add($"Order {strKey}: Istarbeit '{strWork}' {strUnit} is not a usable duration, SetDuration not written.");
        }
      }
      return strRetval;
    }

    internal static JArray parseSheet(string strSheetJson)
    {
      // DateParseHandling.None keeps ISO date text as text; the default would turn it into a date and (string) would render it as MM/dd/yyyy
      using (JsonTextReader Reader = new JsonTextReader(new System.IO.StringReader(strSheetJson)) { DateParseHandling = DateParseHandling.None })
      {
        return JArray.Load(Reader);
      }
    }

    private static string getExternalKey(string strOrderNumber, string strOperation)
    {
      return $"{strOrderNumber}|{strOperation}";
    }

    // Reads the sheet through the runtime FileReader's xlsx: addressing. A bare 'xlsx:$.Tabelle1'
    // address (no row/column suffix) resolves to the whole sheet as one JSON array.
    // bInvokeReadCommand forces a fresh read from the driver rather than a possibly-stale cached value.
    private Task<JArray> readExcelRowsAsync(IKernelAccess Kernel, CancellationToken Token)
    {
      INode nNode = Kernel.NodeSpace.queryNode(Node => Node.hasProperty("DeviceId", m_ExcelReaderDeviceId) &&
                                                       Node.Name == SheetDataNodeName);
      if (nNode == null)
      {
        throw new InvalidOperationException($"Data node '{SheetDataNodeName}' not found on device '{m_ExcelReaderDeviceId}'.");
      }

      IDataNode<string> DataNode = (IDataNode<string>)nNode;
      EDataState eState = DataNode.requestValue(out string strSheetJson, true);
      if (eState != EDataState.Good)
      {
        throw new InvalidOperationException($"Could not read Excel sheet data. State = '{eState}'.");
      }

      return Task.FromResult(parseSheet(strSheetJson));
    }

    private static string readText(JObject jRow, string strColumn)
    {
      return ((string)jRow[strColumn] ?? "").Trim();
    }

    private static readonly Guid m_ExcelReaderDeviceId = Guid.Parse("dc89beab-c280-43af-a626-99681beefffc");

    private static readonly Guid m_FacilityComponentCollectionId = Guid.Parse("6ccc7179-9125-4b4b-baa4-9849351c1187");

    private static readonly Guid m_MaintenanceJobCollectionId = Guid.Parse("b42394d1-130a-485c-8a7e-7bfd82396a08");
  }
}
