using System;
using System.Collections.Generic;
using System.Text;

namespace MintPlayer.SourceGenerators.Attributes
{
    [AttributeUsage(AttributeTargets.Assembly)]
    public class RegisterExtensionAttribute : Attribute
    {
        public RegisterExtensionAttribute(string methodName) { }
    }
}
