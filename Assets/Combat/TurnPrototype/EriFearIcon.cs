using UnityEngine;

/// <summary>Small vector eye for Fear; drawn as overlay UI rather than a world sprite.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class EriFearIcon : UnityEngine.UI.MaskableGraphic
{
    public enum Cue { Fear, Weakness, Damage }
    public Cue Meaning = Cue.Fear;
    [Header("Editable icon appearance")]
    public Color EyeColor = new Color(0.83f,0.59f,1,1);
    public Color OutlineColor = new Color(0.04f,0.05f,0.08f,1);
    public Color WeaknessColor = new Color(1,0.85f,0.55f);
    public Color DamageColor = new Color(0.75f,0.92f,1);
    [Tooltip("Optional replacement artwork for the entire icon. Leave empty for the vector icon.")]
    public Sprite CustomSprite;
    public override Texture mainTexture => CustomSprite!=null?CustomSprite.texture:base.mainTexture;
    public static void ConfigureOverlay(Canvas canvas,int order)
    {
        canvas.renderMode=RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting=true;
        int highest=int.MinValue;
        foreach(var layer in SortingLayer.layers)
            if(layer.value>highest){highest=layer.value;canvas.sortingLayerID=layer.id;}
        canvas.sortingOrder=order;
    }
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
    {
        mesh.Clear();
        if(CustomSprite!=null)
        {
            Rect r=rectTransform.rect;var uv=UnityEngine.Sprites.DataUtility.GetOuterUV(CustomSprite);
            mesh.AddVert(new Vector2(r.xMin,r.yMin),color,new Vector2(uv.x,uv.y));
            mesh.AddVert(new Vector2(r.xMin,r.yMax),color,new Vector2(uv.x,uv.w));
            mesh.AddVert(new Vector2(r.xMax,r.yMax),color,new Vector2(uv.z,uv.w));
            mesh.AddVert(new Vector2(r.xMax,r.yMin),color,new Vector2(uv.z,uv.y));
            mesh.AddTriangle(0,1,2);mesh.AddTriangle(2,3,0);return;
        }
        Vector2[] eye={new Vector2(-1,0),new Vector2(-0.55f,0.5f),new Vector2(0,0.65f),new Vector2(0.55f,0.5f),new Vector2(1,0),new Vector2(0.55f,-0.5f),new Vector2(0,-0.65f),new Vector2(-0.55f,-0.5f)};
        Rect full=rectTransform.rect;
        Rect eyeRect=Meaning==Cue.Fear?full:new Rect(full.x,full.y,full.width*0.58f,full.height);
        Polygon(mesh,eye,eyeRect,1,OutlineColor);
        Polygon(mesh,eye,eyeRect,0.79f,EyeColor);
        Vector2[] pupil={new Vector2(0,0.46f),new Vector2(0.19f,0),new Vector2(0,-0.46f),new Vector2(-0.19f,0)};
        Polygon(mesh,pupil,eyeRect,1,OutlineColor);
        if(Meaning==Cue.Fear)return;
        var cueRect=new Rect(full.x+full.width*0.64f,full.y+full.height*0.1f,full.width*0.36f,full.height*0.8f);
        if(Meaning==Cue.Weakness)
        {
            Vector2[] shaft={new Vector2(-0.35f,1),new Vector2(0.35f,1),new Vector2(0.35f,-0.1f),new Vector2(-0.35f,-0.1f)};
            Vector2[] tip={new Vector2(-1,0.15f),new Vector2(1,0.15f),new Vector2(0,-1)};
            Polygon(mesh,shaft,cueRect,1,OutlineColor);Polygon(mesh,tip,cueRect,1,OutlineColor);
            Polygon(mesh,shaft,cueRect,0.72f,WeaknessColor);Polygon(mesh,tip,cueRect,0.72f,WeaknessColor);
        }
        else
        {
            var burst=new Vector2[16];
            for(int i=0;i<16;i++){float a=i*Mathf.PI/8;burst[i]=new Vector2(Mathf.Cos(a),Mathf.Sin(a))*(i%2==0?1:0.44f);}
            Polygon(mesh,burst,cueRect,1,OutlineColor);Polygon(mesh,burst,cueRect,0.72f,DamageColor);
        }
    }
    private void Polygon(UnityEngine.UI.VertexHelper mesh,Vector2[] points,Rect r,float scale,Color tint)
    {
        int first=mesh.currentVertCount;
        tint*=color;
        mesh.AddVert(r.center,tint,Vector2.zero);
        foreach(var p in points)mesh.AddVert(r.center+Vector2.Scale(p,new Vector2(r.width,r.height))*0.5f*scale,tint,Vector2.zero);
        for(int i=0;i<points.Length;i++)mesh.AddTriangle(first,first+1+i,first+1+(i+1)%points.Length);
    }
}
