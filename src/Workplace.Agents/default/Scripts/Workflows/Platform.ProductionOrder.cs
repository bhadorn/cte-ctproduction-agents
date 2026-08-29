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
  public class TJobOperation : TAbstractPipelineOperationScriptObject
  {
  
    private const int JobState_Ready = 200;
    private const int JobState_Setup = 300;
    private const int JobState_Production = 400;
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
            Retval.Data.AddRange(await startAsync(WorkplaceId, Device, JobData, false, Token).ConfigureAwait(false));
            break;

          case "startSetup":
            Retval.Data.AddRange(await startAsync(WorkplaceId, Device, JobData, true, Token).ConfigureAwait(false));
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
    private async Task<IEnumerable<TEntityDataJM>> startAsync(Guid WorkplaceId, IGroupRelation Device, TEntityDataJM JobData, bool bSetupOnly, CancellationToken Token)
    {
      List<TEntityDataJM> lstRetval = new List<TEntityDataJM>();
      TEntityDataJM JobToStart = new TEntityDataJM()
      {
        Id = JobData.Id,
        CollectionId = JobData.CollectionId
      };
      JobToStart.Fields["State"] = bSetupOnly ? JobState_Setup : JobState_Production;
      JobToStart.Fields["Action"] = JobAction_Started;
      
      //Explicitly set the workplace
      JobToStart.Fields["WorkplaceId"] = WorkplaceId;
      
      //Explicitly set the total quantity (this is a writeback in case the order defines the TotalQuantity)
      JobToStart.Fields["TotalQuantity"] = JobData.Fields["TotalQuantity"];
      
      Guid ProductionOrderId = JobData.readField<Guid>("ProductionOrder", Guid.Empty);
      Guid ProductionJobId = JobData.Id;
      
      (IDataNode<Guid> ProductionJob, 
       IDataNode<Guid> LastProductionJob,
       IDataNode<Guid> ProductionOrder,
       IDataNode<double> ProducedQuantity,
       IDataNode<double> BadQuantity,
       IDataNode<double> TotalQuantity,
       IDataNode<double> SetCycleTime,
       IDataNode<double> MaxChangeOverTime,
       IDataNode<int> JobState) = getJobDataNodesGroup(Device);
      
      //Pause the previous job before starting the new job
      // All data are written back to this job
      Guid PreviousJobId = ProductionJob.Value;
      if (PreviousJobId != Guid.Empty)
      {
        //Stops the job on the workplace
        JobState.passValue(JobState_Ready);
        TEntityDataJM JobToPause = new TEntityDataJM()
        {
          Id = ProductionJob.Value,
          CollectionId = JobData.CollectionId
        };
        JobToPause.Fields["ProducedQuantity"] = ProducedQuantity.Value;
        JobToPause.Fields["BadQuantity"] = BadQuantity.Value;
        JobToPause.Fields["State"] = JobState_Ready;
        JobToPause.Fields["Action"] = JobAction_Paused;
        lstRetval.Add(JobToPause);
        writeInfo($"Production job '{ProductionJob.Value}' paused.");
      }
      
      //Write the new job data. A job that held the workplace is named as the one that left, so the
      // sample this batch produces closes its time - starting a job over a running one is the second
      // way a job leaves a workplace, next to stopAsync. Guid.Empty when the workplace was free, and
      // also when this same job is being promoted from setup to production: it is not leaving.
      LastProductionJob.passValue(PreviousJobId != ProductionJobId ? PreviousJobId : Guid.Empty);
      ProductionJob.passValue(ProductionJobId);
      ProductionOrder.passValue(ProductionOrderId);
      ProducedQuantity.passValue(JobData.readField<double>("ProducedQuantity", 0));
      BadQuantity.passValue(JobData.readField<double>("BadQuantity", 0));
      TotalQuantity.passValue(JobData.readField<double>("TotalQuantity", 0));
      SetCycleTime.passValue(JobData.readField<double>("SetCycleTime", 0));
      MaxChangeOverTime.passValue(JobData.readField<TimeSpan>("MaxChangeOverTime", TimeSpan.Zero).TotalMinutes);
      
      //Starts the job on the workplace
      JobState.passValue((int)JobToStart.Fields["State"]); 
      
      //Writeback the quantity counter (they might be corrected by input arguments)
      JobToStart.Fields["ProducedQuantity"] = JobData.Fields["ProducedQuantity"];
      JobToStart.Fields["BadQuantity"] = JobData.Fields["BadQuantity"];
      
      if (bSetupOnly)
      {
        writeInfo($"Production job '{JobData.readField<string>("Name", "")}' started for setup.");
      }
      else
      {
        writeInfo($"Production job '{JobData.readField<string>("Name", "")}' started for production.");
      }
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
      (IDataNode<Guid> ProductionJob, 
       IDataNode<Guid> LastProductionJob,
       IDataNode<Guid> ProductionOrder,
       IDataNode<double> ProducedQuantity,
       IDataNode<double> BadQuantity,
       IDataNode<double> TotalQuantity,
       IDataNode<double> SetCycleTime,
       IDataNode<double> MaxChangeOverTime,
       IDataNode<int> JobState) = getJobDataNodesGroup(Device);

      //Stops the job on the workplace. The job state distinguishes a pause from a stop, and the
      // ending job's id moves to LastProductionJob so the sample this batch produces still names it
      // once ProductionJob is cleared below.
      // The id comes from the platform, not from ProductionJob.Value: the platform is the system of
      // record for which job runs where. An agent that lost its memory holds no job id, and the
      // recovery is exactly to interrupt and restart the job - reading the id off the workplace
      // would emit a closing sample naming nothing and lose the job's last segment.
      JobState.passValue(bPauseOnly ? JobState_Ready : JobState_Done);
      LastProductionJob.passValue(JobData.Id);
      
      //Only write back if the job matches
      if (ProductionJob.Value == JobData.Id)
      {
        //Check if the platform sends some quantity corrections
        if (JobData.readField<bool>("CorrectQuantity", false))
        {
          ProducedQuantity.passValue(JobData.readField<double>("ProducedQuantity", ProducedQuantity.Value));
          BadQuantity.passValue(JobData.readField<double>("BadQuantity", BadQuantity.Value));
          Logger.writeInfo($"Quantity corrected to: ProducedQuantity={ProducedQuantity.Value}; BadQuantity={BadQuantity.Value}");

          //wait to send data back to platform as stream
          await Task.Delay(1000); 
        }
 
        Retval.Fields["ProducedQuantity"] = ProducedQuantity.Value;
        Retval.Fields["BadQuantity"] = BadQuantity.Value;
      }
      else
      {
        Logger.writeWarning($"Job does not match. Reading quantity data ignored.");
      }
      
      //Clear the memory
      ProductionJob.passValue(Guid.Empty);
      ProductionOrder.passValue(Guid.Empty);
      ProducedQuantity.passValue(0);
      BadQuantity.passValue(0);
      TotalQuantity.passValue(0);
      SetCycleTime.passValue(0);
      MaxChangeOverTime.passValue(0);
      
      if (bPauseOnly)
      {
        writeInfo($"Production job '{JobData.readField<string>("Name", "")}' paused.");
      }
      else
      {
        writeInfo($"Production job '{JobData.readField<string>("Name", "")}' stopped.");
      }
      return await Task.FromResult(Retval).ConfigureAwait(false);
    }

    //Gets the current job relation
    private static (IDataNode<Guid> ProductionJob, 
                    IDataNode<Guid> LastProductionJob,
                    IDataNode<Guid> ProductionOrder,
                    IDataNode<double> ProducedQuantity,
                    IDataNode<double> BadQuantity,
                    IDataNode<double> TotalQuantity,
                    IDataNode<double> SetCycleTime,
                    IDataNode<double> MaxChangeOverTime,
                    IDataNode<int> JobState) getJobDataNodesGroup(IGroupRelation Device)
    {
      IGroupRelation nCurrentJobGroup = (IGroupRelation)Device.queryNode(n => n.Name == "CurrentJob");
      if (nCurrentJobGroup == null)
      {
        throw new ArgumentException($"CurrentJob group not found.");
      }
      
      return ((IDataNode<Guid>)nCurrentJobGroup.queryNode(n => n.Name == "ProductionJob"), 
              (IDataNode<Guid>)nCurrentJobGroup.queryNode(n => n.Name == "LastProductionJob"),
              (IDataNode<Guid>)nCurrentJobGroup.queryNode(n => n.Name == "ProductionOrder"),
              (IDataNode<double>)nCurrentJobGroup.queryNode(n => n.Name == "ProducedQuantity"),
              (IDataNode<double>)nCurrentJobGroup.queryNode(n => n.Name == "BadQuantity"),
              (IDataNode<double>)nCurrentJobGroup.queryNode(n => n.Name == "TotalQuantity"),
              (IDataNode<double>)nCurrentJobGroup.queryNode(n => n.Name == "SetCycleTime"),
              (IDataNode<double>)nCurrentJobGroup.queryNode(n => n.Name == "MaxChangeOverTime"),
              (IDataNode<int>)nCurrentJobGroup.queryNode(n => n.Name == "JobState"));
    }
  }
}
