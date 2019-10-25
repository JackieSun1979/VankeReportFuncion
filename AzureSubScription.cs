using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace FunctionGenerateReportData
{
    public class AzureSubScription
    {
        string subScriptionName;
        string subScriptionID;
        IList<AzureResourceGroup> resourceGroups;

        public string SubScriptionName { get => subScriptionName; set => subScriptionName = value; }
        public string SubScriptionID { get => subScriptionID; set => subScriptionID = value; }
        public IList<AzureResourceGroup> ResourceGroups { get => resourceGroups; set => resourceGroups = value; }
    }

    public class AzureResourceGroup
    {
        string name;
        string projectName;

        public string Name { get => name; set => name = value; }
        public string ProjectName { get => projectName; set => projectName = value; }
    }
}
