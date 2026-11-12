using Aholo3D.Core;
using System.Text.Json;
namespace Aholo3D.Tests;

public sealed class CreationDraftValidationTests
{
    private static CreationDraft Draft(params (string Key,string[] Values)[] pairs)=>new(pairs.Select(p=>new KeyValuePair<string,string[]>(p.Key,p.Values)));
    [Theory][InlineData("1e-")][InlineData("NaN")][InlineData("Infinity")][InlineData("1,000")][InlineData("300000.5")][InlineData("300001")][InlineData("-1")]
    public void InvalidFaceCountsRemainEditableAndCannotProduceRequest(string value)
    {
        var draft=Draft(("prompt:text",new[]{"chair"}),("faceCount:text",new[]{value}));
        var result=CreationDraftValidation.Evaluate("lux.text-to-3d",draft);
        Assert.False(result.IsValid);Assert.Null(result.Request);Assert.Contains("faceCount",result.Errors.Keys);
        Assert.Equal(value,draft.Values["faceCount:text"][0]);
    }
    [Theory][MemberData(nameof(ServiceApiTests.Creates),MemberType=typeof(ServiceApiTests))]
    public void EveryOperationValidFixtureRoundTripsThroughDraftParser(string operation,string json)
    {
        using var document=JsonDocument.Parse(json);var fields=new Dictionary<string,string[]>();
        foreach(var property in document.RootElement.EnumerateObject())
        {
            string field=property.Name;var value=property.Value;
            if(field=="name"){fields["$name"]=new[]{value.GetString()!};continue;}
            if(field=="resources")
            {
                // Public resource objects are represented by local file selection in this form.
                fields[field+":files"]=value.EnumerateArray().Select(v=>new Uri(v.GetProperty("url").GetString()!).AbsolutePath).ToArray();continue;
            }
            if(CreationDraftValidation.IsFileField(field))
            {fields[field+":url"]=new[]{value.ValueKind==JsonValueKind.Array?string.Join("\n",value.EnumerateArray().Select(x=>x.GetString())):value.GetString()!};continue;}
            var schema=ServiceOperations.ResolveSchema(operation,ServiceOperations.RequestSchema(operation).GetProperty("properties").GetProperty(field));
            if(field=="transformationMatrix"){fields[field+":enabled"]=new[]{"true"};fields[field+":matrix"]=value.EnumerateArray().SelectMany(r=>r.EnumerateArray().Select(x=>x.GetRawText())).ToArray();}
            else if(schema.TryGetProperty("enum",out _)||value.ValueKind is JsonValueKind.True or JsonValueKind.False)fields[field+":choice"]=new[]{value.ValueKind==JsonValueKind.String?value.GetString()!:value.GetRawText()};
            else if(value.ValueKind==JsonValueKind.Array)fields[field+":selected"]=value.EnumerateArray().Select(x=>x.GetString()!).ToArray();
            else fields[field+":text"]=new[]{value.ValueKind==JsonValueKind.String?value.GetString()!:value.GetRawText()};
        }
        var result=CreationDraftValidation.Evaluate(operation,new(fields));
        Assert.True(result.IsValid,string.Join("; ",result.Errors.Values));_=ServiceRequest.Bind(result.Request!,ServiceRequest.Placeholder);
    }
    [Fact] public void WorldRequiresPromptOrReferenceAndDoesNotReadSelectedFiles()
    {
        Assert.False(CreationDraftValidation.Evaluate("world.generate",Draft()).IsValid);
        var result=CreationDraftValidation.Evaluate("world.generate",Draft(("resources:files",new[]{"missing-input.png"})));
        Assert.True(result.IsValid);Assert.Equal("missing-input.png",Assert.Single(result.Request!.Files!).Paths[0]);
    }
    [Fact] public void FileAndUrlConflictIsAttachedToInput()
    {
        var result=CreationDraftValidation.Evaluate("lux.img-to-3d",Draft(("img:files",new[]{"x.png"}),("img:url",new[]{"https://example.com/x.png"})));
        Assert.Contains("img",result.Errors.Keys);Assert.Null(result.Request);
    }
    [Fact] public void OptionalBlankNumberDoesNotSendZeroAndWholeNumberKeepsIntegerType()
    {
        var result=CreationDraftValidation.Evaluate("lux.text-to-3d",Draft(("prompt:text",new[]{"chair"}),("faceCount:text",new[]{"300000"}),("enablePbr:choice",new[]{"false"})));
        Assert.True(result.IsValid);Assert.Equal(300000,result.Request!.Parameters.GetProperty("faceCount").GetInt64());Assert.False(result.Request.Parameters.GetProperty("enablePbr").GetBoolean());
        Assert.Null(CreationDraftValidation.Number("",false));
    }
    [Theory][InlineData("NaN")][InlineData("1e999")][InlineData("1,25")][InlineData("1e-")]
    public void NonfiniteOrAmbiguousDecimalIsRejected(string value)=>Assert.Throws<ArgumentException>(()=>CreationDraftValidation.Number(value,false));
    [Fact] public void SchemaHintsUseServiceFaceLimitAndNoGeometryLimit()
    {Assert.Contains("300000",CreationDraftValidation.Hint("lux.text-to-3d","faceCount"));Assert.Equal(1.25,CreationDraftValidation.Number("1.25",false));}
}
