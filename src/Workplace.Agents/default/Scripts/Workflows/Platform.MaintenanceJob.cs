/*****************************************************************************
 * Copyright (C) by CyberTech Engineering 2025 – www.cybertech.swiss         *
 *****************************************************************************
 * Project: HumanOS (R)
 * Date   : 2025
 *****************************************************************************
 * License:                                                                  *
 *   This library is protected software; you are not allowed to redistribute *
 *   whole or part of it to other companies or external persons without the  *
 *   authorization of the CEO CyberTech Engineering GmbH.                    *
 *****************************************************************************/

using HumanOS.Kernel;
using HumanOS.Kernel.DataModel;
using HumanOS.Kernel.Processing;
using HumanOS.Kernel.Utils;
using HumanOS.Kernel.Workflow.Activity;
using HumanOS.PeMiL.PlatformBinding.JsonModels;
using HumanOS.PeMiL.PlatformBinding.Pipelines;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HumanOS.IoT.Designer.Library.Scripts
{
  /// <summary>
  /// Example of a workflow operation script
  /// </summary>
  public class TMaintenanceJobOperation : TAbstractPipelineOperationScriptObject
  {
  
    private const int JobState_Ready = 200;
    private const int JobState_Running = 400;
    private const int JobState_Done = 900;
    
    private const int JobAction_Started = 1;
    private const int JobAction_Paused = 2;
    private const int JobAction_Done = 3;
    
    ///<see cref="TAbstractPipelineOperationScriptObject"/>
    protected override async Task<TPipelineOutputJM> runJobAsync(IKernelAccess Kernel,
                                                                 IActivity Activity,
                                                                 TPipelineExecutionContext PipelineContext,
                                                                 CancellationToken Token)
    {
      TPipelineOutputJM Retval = new TPipelineOutputJM();

      try
      {
        Dictionary<string, object> dicArguments = JsonConvert.DeserializeObject<Dictionary<string, object>>(PipelineContext.TriggerActionArguments);

        if (!dicArguments.TryGetValue("WorkplaceId", out object WorkplaceIdObject))
        {
          throw new ArgumentException("Missing argument 'WorkplaceId'.");
        }
        Guid WorkplaceId = Guid.Parse(WorkplaceIdObject.ToString());
        
        IGroupRelation Device = await getDeviceAsync(Kernel, n => n.GlobalId == WorkplaceId || n.hasProperty("WorkplaceId", WorkplaceId), Token).ConfigureAwait(false);
        writeInfo($"Device found '{Device.Name}' ({Device.GlobalId})");
        
        TEntityDataJM JobData = JsonConvert.DeserializeObject<List<TEntityDataJM>>(PipelineContext.DataInput).First();

        switch (PipelineContext.TriggerAction)
        {
          case "start":
            Retval.Data.AddRange(await startAsync(WorkplaceId, Device, JobData, Token).ConfigureAwait(false));
            break;

          case "stop":
            Retval.Data.Add(await stopAsync(Device, JobData, false, Token).ConfigureAwait(false));
            break;

          case "pause":
            Retval.Data.Add(await stopAsync(Device, JobData, true, Token).ConfigureAwait(false));
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
      return await Task.FromResult(Retval);
    }
    
    //Starts the job on the device
    private async Task<IEnumerable<TEntityDataJM>> startAsync(Guid WorkplaceId, IGroupRelation Device, TEntityDataJM JobData, CancellationToken Token)
    {
      List<TEntityDataJM> lstRetval = new List<TEntityDataJM>();
      TEntityDataJM JobToStart = new TEntityDataJM()
      {
        Id = JobData.Id,
        CollectionId = JobData.CollectionId
      };
      JobToStart.Fields["State"] = JobState_Running;
      JobToStart.Fields["Action"] = JobAction_Started;
      
      //Explicitly set the workplace
      JobToStart.Fields["WorkplaceId"] = WorkplaceId;
      
      Guid MaintenanceJobId = JobData.Id;
      
      (IDataNode<Guid> MaintenanceJob,
       IDataNode<int> MaintenanceState) = getMachineStateNodesGroup(Device);
      
      //Pause the previous job before starting the new job
      // All data are written back to this job
      if (MaintenanceJob.Value != Guid.Empty)
      {
        //Stops the job on the workplace
        MaintenanceState.passValue(0);
        TEntityDataJM JobToPause = new TEntityDataJM()
        {
          Id = MaintenanceJob.Value,
          CollectionId = JobData.CollectionId
        };
        JobToPause.Fields["State"] = JobState_Ready;
        JobToPause.Fields["Action"] = JobAction_Paused;
        lstRetval.Add(JobToPause);
        writeInfo($"Maintenance job '{MaintenanceJob.Value}' paused.");
      }
      
      //Write the new job data
      MaintenanceJob.passValue(MaintenanceJobId);
      
      //Starts the job on the workplace
      MaintenanceState.passValue(1); 
      
      writeInfo($"Maintenance job '{JobData.readField<string>("Name", "")}' started.");
      
      lstRetval.Add(JobToStart);
      return await Task.FromResult(lstRetval).ConfigureAwait(false);
    }
    
    //Stops the job
    private async Task<TEntityDataJM> stopAsync(IGroupRelation Device, TEntityDataJM JobData, bool bPauseOnly, CancellationToken Token)
    {
      TEntityDataJM Retval = new TEntityDataJM()
      {
        Id = JobData.Id,
        CollectionId = JobData.CollectionId
      };
      Retval.Fields["State"] = bPauseOnly ? JobState_Ready : JobState_Done;
      Retval.Fields["Action"] = bPauseOnly ? JobAction_Paused : JobAction_Done;

       (IDataNode<Guid> MaintenanceJob,
       IDataNode<int> MaintenanceState) = getMachineStateNodesGroup(Device);

      //Stops the job on the workplace
      MaintenanceState.passValue(0);
      
      //Clear the memory
      MaintenanceJob.passValue(Guid.Empty);
      
      if (bPauseOnly)
      {
        writeInfo($"Maintenance job '{JobData.readField<string>("Name", "")}' paused.");
      }
      else
      {
        writeInfo($"Maintenance job '{JobData.readField<string>("Name", "")}' stopped.");
      }
      return await Task.FromResult(Retval).ConfigureAwait(false);
    }

    //Gets the machine state relation
    private static (IDataNode<Guid> ProductionJob, 
                    IDataNode<int> MaintenanceState) getMachineStateNodesGroup(IGroupRelation Device)
    {
      IGroupRelation nMachineStateGroup = (IGroupRelation)Device.queryNode(n => n.Name == "MachineState");
      if (nMachineStateGroup == null)
      {
        throw new ArgumentException($"MachineState group not found.");
      }
      
      return ((IDataNode<Guid>)nMachineStateGroup.queryNode(n => n.Name == "MaintenanceJob"), 
              (IDataNode<int>)nMachineStateGroup.queryNode(n => n.Name == "MaintenanceState"));
    }
  }
}
