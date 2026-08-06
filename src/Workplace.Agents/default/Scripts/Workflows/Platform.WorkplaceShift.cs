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
using HumanOS.Kernel.DataModel;
using HumanOS.Kernel.Workflow.Activity;
using HumanOS.PeMiL.PlatformBinding.JsonModels;
using HumanOS.PeMiL.PlatformBinding.Pipelines;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HumanOS.IoT.Designer.Library.Scripts
{
  /// <summary>
  /// Pipeline scripts to process workplace shifts
  /// </summary>
  public class TJobOperation : TAbstractPipelineOperationScriptObject
  {
    ///<see cref="TAbstractPipelineOperationScriptObject"/>
    protected override async Task<TPipelineOutputJM> runJobAsync(IKernelAccess Kernel,
                                                                 IActivity Activity,
                                                                 TPipelineExecutionContext PipelineContext,
                                                                 CancellationToken Token)
    {
      TPipelineOutputJM Retval = new TPipelineOutputJM();
      try
      {
        TEntityDataJM JobData = JsonConvert.DeserializeObject<List<TEntityDataJM>>(PipelineContext.DataInput).First();

        Guid WorkplaceId = JobData.readField<Guid>("WorkplaceId", Guid.Empty);
        
        IGroupRelation Device = await getDeviceAsync(Kernel, n => n.GlobalId == WorkplaceId || n.hasProperty("WorkplaceId", WorkplaceId), Token).ConfigureAwait(false);
        writeInfo($"Device found '{Device.Name}' ({Device.GlobalId})");


        switch (PipelineContext.TriggerAction)
        {
          case "create": //fall through
          case "update":
            await setWorkplaceShiftSettingsAsync(Kernel, Device, JobData, Token).ConfigureAwait(false);
            break;

          default:
            throw new ArgumentException($"Invalid trigger action '{PipelineContext.TriggerAction}'.");
        }
      }
      catch(Exception Exc) when (Exc.isNotCancelException())
      {
        writeError($"{Exc.Message}\n{Exc.StackTrace}");
        throw;
      }
      return Retval;
    }

    // sets the workplace shift settings for workplaces
    private async Task setWorkplaceShiftSettingsAsync(IKernelAccess Kernel, IGroupRelation Device, TEntityDataJM JobData, CancellationToken Token)
    {
      writeInfo($"Set workplace shift data for workplace '{JobData.readField<string>("Name", "")}' ({JobData.Id}).");

      IGroupRelation nMachineStateGroup = (IGroupRelation)Device.queryNode(n => n.Name == "MachineState");
      if (nMachineStateGroup == null)
      {
        throw new ArgumentException($"MachineState group not found.");
      }
      IDataNode<string> nWorkplaceShift = (IDataNode<string>)nMachineStateGroup.queryNode(n => n.Name == "WorkShiftSetting");
      if (nWorkplaceShift != null)
      {
        JObject jTimeShifts = new JObject();
        jTimeShifts.Add("Monday",    JobData.readField<JArray>("ShiftOnMondayExt", null));
        jTimeShifts.Add("Tuesday",   JobData.readField<JArray>("ShiftOnTuesdayExt", null));
        jTimeShifts.Add("Wednesday", JobData.readField<JArray>("ShiftOnWednesdayExt", null));
        jTimeShifts.Add("Thursday",  JobData.readField<JArray>("ShiftOnThursdayExt", null));
        jTimeShifts.Add("Firday",    JobData.readField<JArray>("ShiftOnFridayExt", null));
        jTimeShifts.Add("Saturday",  JobData.readField<JArray>("ShiftOnSaturdayExt", null));
        jTimeShifts.Add("Sunday",    JobData.readField<JArray>("ShiftOnSundayExt", null));
        nWorkplaceShift.passValue($"{jTimeShifts}");
        writeInfo($"Workplace shifts set to WorkShiftSetting data node.");
      }
      else
      {
        writeInfo($"Data node 'WorkShiftSetting' not found. Ignored.");
      }
    }
  }
}
