namespace Knapcode.FactorioTools.OilField;

public class HeatPipe : GridEntity
{
    public HeatPipe(int id) : base(id)
    {
    }

#if ENABLE_GRID_TOSTRING
    public override string Label => "h";
#endif
}
