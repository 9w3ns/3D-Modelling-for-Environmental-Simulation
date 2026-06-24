using Rhino;

namespace EnvAnalysisCore
{
    ///<summary>
    /// Every Rhino plugin must have exactly one PlugIn-derived class.
    ///</summary>
    public class EnvAnalysisCorePlugIn : Rhino.PlugIns.PlugIn
    {
        public EnvAnalysisCorePlugIn()
        {
            Instance = this;
        }

        public static EnvAnalysisCorePlugIn Instance { get; private set; }

        // You can override methods here to handle plugin load/unload events.
    }
}