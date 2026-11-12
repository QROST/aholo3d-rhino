namespace Aholo3D.Core;
public sealed class AssetValidationException : Exception
{
 public string Code { get; }
 public AssetValidationException(string code,string message):base(message){Code=code;}
 public AssetValidationException(string code,string message,Exception inner):base(message,inner){Code=code;}
}
