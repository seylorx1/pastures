using Godot;
using Godot.Collections;

namespace Pastures;

[Tool, GlobalClass]
public partial class TerrainGeneratorLOD : Resource
{
    [Export] public int LOD_Level
    {
        get => _lodLevel;
        set => _lodLevel = Mathf.Max(value, 0);
    }

    [Export] public float TerrainSinkDistance
    {
        get => _terrainSinkDistance;
        set
        {
            _terrainSinkDistance = value;
            UpdateMaterialProperties();
        }
    }

    [ExportGroup("Visibility Range")]
    [Export]
    public float Begin
    {
        get => _beginVisibility;
        set => _beginVisibility = LOD_Level == 0 ? 0f : Mathf.Max(value, 0f);
    }

    [Export]
    public float BeginMargin
    {
        get => _beginVisibilityMargin;
        set => _beginVisibilityMargin = LOD_Level == 0 ? 0f : Mathf.Max(value, 0f);
    }

    [Export]
    public float End
    {
        get => _endVisibility;
        set => _endVisibility = Mathf.Max(value, Begin);
    }

    [Export]
    public float EndMargin
    {
        get => _endVisibilityMargin;
        set => _endVisibilityMargin = Mathf.Max(value, 0f);
    }

    private int _lodLevel = 0;
    private float _terrainSinkDistance = 0f;
    private float _beginVisibility = 0f;
    private float _beginVisibilityMargin = 0f;
    private float _endVisibility = 0f;
    private float _endVisibilityMargin = 0f;

    [Export] public ShaderMaterial LOD_Material {get; set;}

    public override void _ValidateProperty(Dictionary property)
    {
        if(property["name"].AsStringName() == PropertyName.LOD_Material)
        {
            property["usage"] = (int)PropertyUsageFlags.ReadOnly;
        }        
    }

    public void UpdateMaterialProperties()
    {
        if(LOD_Material == null)
            return;

        LOD_Material.SetShaderParameter("sinkDistance", TerrainSinkDistance);
    }
}