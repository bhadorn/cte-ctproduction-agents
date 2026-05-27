/*****************************************************************************
 * Copyright (C) by CyberTech Engineering 2022 – www.cybertech.swiss         *
 *****************************************************************************
 * Project: HumanOS (R)
 * Date   : 2022
 *****************************************************************************
 * License:                                                                  *
 *   This library is protected software; you are not allowed to redistribute *
 *   whole or part of it to other companies or external persons without the  *
 *   authorization of the CEO CyberTech Engineering GmbH.                    *
 *****************************************************************************/

using HumanOS.Kernel.Processing;
using System.Linq;
using System;

namespace HumanOS.UHAL.HeidenhainControl.Scripts
{
  /// <summary>
  /// Processes the program header of the main program
  /// </summary>
  public class TMainProgramHeaderProcessorScript : TAbstractProcessingScriptObject
  {
    /// <see cref="TAbstractProcessingScriptObject"/>
    public override void process(IProcessingNode Processor)
    {
      // get input values
      string strMainHeader = Processor.getProperty<string>("In_MainProgramHeader");

      string strProductName = "";
      string strProcessStep = "";

      // calculate program name
      if (strMainHeader != null)
      {
        //Logger.writeInfo(strMainHeader);
        string[] astrLines = strMainHeader.Split('\n');
        strProductName = astrLines.FirstOrDefault(n => n.Contains(";PRODUCT:"));
        strProcessStep = astrLines.FirstOrDefault(n => n.Contains(";STEP:"));

        if (strProductName.isEmpty()) { strProductName = ""; }
        else { strProductName = strProductName.Split(";PRODUCT:").Last(); }
        if (strProcessStep.isEmpty()) { strProcessStep = ""; }
        else { strProcessStep = strProcessStep.Split(";STEP:").Last(); }
      }

      Processor.setProperty<string>("Out_ProductName", strProductName.trimWhiteSpaces());
      Processor.setProperty<string>("Out_ProductionStep", strProcessStep.trimWhiteSpaces());
    }
  }
}
