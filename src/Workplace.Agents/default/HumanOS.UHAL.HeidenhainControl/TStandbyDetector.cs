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

using HumanOS.Kernel.Processing;
using HumanOS.Kernel.DataModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HumanOS.UHAL.HeidenhainControl.Scripts
{
  /// <summary>
  /// Detects whether the machine is in standby (end-of-shift shutdown). The operator triggers
  /// this by pressing NC-Stop/Emergency-Stop at the end of the shift, which raises one of a
  /// fixed set of NC alarms rather than indicating a real fault. This lets a planned standby
  /// be reported as "Power Off" instead of "Disturbance".
  /// </summary>
  public class TStandbyDetector : TAbstractProcessingScriptObject
  {
    #region Constants

    /// <summary>
    /// Source name of alarms of the PLC error group (DNC_ERROR_GROUP.DNC_EG_PLC)
    /// </summary>
    private const string PlcGroupName = "DNC_EG_PLC";

    /// <summary>
    /// Message text of the Feierabend-Abschaltung, compared case-insensitively and without hyphens/whitespace
    /// (any error group, independent of the alarm number)
    /// </summary>
    private const string StandbyMessageText = "FEIERABENDABSCHALTUNG";

    #endregion Constants

    #region Implementation of TAbstractProcessingScriptObject

    ///<see cref="TAbstractProcessingScriptObject"/>
    public override void process(IProcessingNode Processor)
    {
      bool bStandbyActive = Processor.getAllAlarmMessages("Alarms").Any(isStandbyAlarm);

      if (m_nbLastStandbyActive != bStandbyActive)
      {
        Logger.writeInfo($"StandbyActive changed to {bStandbyActive}");
        m_nbLastStandbyActive = bStandbyActive;
      }

      Processor.setProperty<bool>("StandbyActive", bStandbyActive);
    }

    #endregion Implementation of TAbstractProcessingScriptObject

    private static bool isStandbyAlarm(TAlarmItem Item)
    {
      string nstrCondition = Item.ConditionName?.Trim();

      return hasStandbyMessageText(Item) ||
             (nstrCondition != null &&
              (m_setStandbyConditions.Contains(nstrCondition) ||
               (string.Equals(Item.SourceName, PlcGroupName, StringComparison.OrdinalIgnoreCase) &&
                m_setStandbyPlcConditions.Contains(nstrCondition))));
    }

    private static bool hasStandbyMessageText(TAlarmItem Item)
    {
      return Item.Message != null &&
             new string(Item.Message.Where(c => c != '-' && !char.IsWhiteSpace(c)).ToArray())
               .Contains(StandbyMessageText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Condition names ("Alarm &lt;number&gt;") of the NC alarms that signal standby
    /// (182 = NOT AUS)
    /// </summary>
    private static readonly HashSet<string> m_setStandbyConditions = new HashSet<string>(
      new[] { 182, 4014, 7014 }.Select(iNumber => $"Alarm {iNumber}"),
      StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Condition names ("Alarm &lt;number&gt;") of the PLC alarms (error group PLC) that signal standby
    /// (62 and 700854 = "700854 Feierabend-Abschaltung!", class Emerg. Stop)
    /// </summary>
    private static readonly HashSet<string> m_setStandbyPlcConditions = new HashSet<string>(
      new[] { 62, 700854 }.Select(iNumber => $"Alarm {iNumber}"),
      StringComparer.OrdinalIgnoreCase);

    private bool? m_nbLastStandbyActive;
  }
}
