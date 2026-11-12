namespace Aholo3D.Core;

/// <summary>Explicit service selection, independent of UI language and secret contents.</summary>
public sealed class Aholo3DRegion
{
    public string Id { get; }
    public Uri ApiBase { get; }
    public string OAuthResource { get; }
    public string OAuthIssuer { get; }
    public string UploadHost { get; }
    internal Aholo3DRegion(string id,string api,string resource,string issuer,string upload)
    {Id=id;ApiBase=new(api);OAuthResource=resource;OAuthIssuer=issuer;UploadHost=upload;}
}
public static class Aholo3DRegions
{
    public const string International="international", China="china";
    private static readonly Aholo3DRegion international=new(International,"https://api.aholo.ai/global/","https://api.aholo.ai","https://www.aholo.ai","ous-sg.kujiale.com");
    private static readonly Aholo3DRegion china=new(China,"https://api.aholo3d.cn/","https://api.aholo3d.cn","https://www.aholo3d.cn","ous-cos.kujiale.com");
    public static Aholo3DRegion Get(string id)=>id switch
    {International=>international,China=>china,_=>throw new ArgumentException("Choose International or China as the service region.")};
    public static string Label(string id)=>Get(id).Id==China?UiText.Get("China"):UiText.Get("International");
}
