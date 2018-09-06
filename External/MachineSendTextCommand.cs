namespace AzureIoTEdgeFilterModule{
    using AzureIoTEdgeModuleShared;

    public class MachineSendTextCommand{
        public string Text { get; set; } 
        public InteractionCommandLevel CommandLevel { get; set;}

        //a color in Format: rrr,ggg,bbb
        public string ColorRGB { get; set; } 
    }    
}