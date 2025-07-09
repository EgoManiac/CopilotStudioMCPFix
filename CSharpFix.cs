public class Script : ScriptBase
{
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
         //Check if the operation ID matches what is specified in the OpenAPI definition of the connector
        if (this.Context.OperationId == "InvokeMCP")
        {
                // Use the context to forward/send an HTTP request
            HttpResponseMessage response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            // Do the transformation if the response was successful, otherwise return error responses as-is
            if (response.IsSuccessStatusCode)
            {
                var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                // Split into lines
                var lines = responseString.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                string eventLine = null;
                string dataLine = null;

                foreach (var line in lines)
                {
                    if (line.StartsWith("event:"))
                    {
                        eventLine = line;
                    }
                    else if (line.StartsWith("data:"))
                    {
                        dataLine = line;
                    }
                }

                if (dataLine != null)
                {
                    var jsonPart = dataLine.Substring("data:".Length).Trim();

                    var result = JObject.Parse(jsonPart);

                    if (result["id"] != null)
                    {
                        result["id"] = result["id"].ToString();
                    }

                    // Reconstruct the SSE format
                    var rebuiltResponse = eventLine + "\n" +
                      "data: " + result.ToString(Newtonsoft.Json.Formatting.None) + "\n\n";

                    //_logger.LogInformation("Transformed SSE JSON: {rebuiltResponse}", rebuiltResponse);

                    response.Content = new StringContent(
                        rebuiltResponse,
                        System.Text.Encoding.UTF8,
                        "text/event-stream"
                    );
                }
            }

            return response;
        }

        //Handle an invalid operation ID
        HttpResponseMessage responsefail = new HttpResponseMessage(HttpStatusCode.BadRequest);
        responsefail.Content = CreateJsonContent($"Unknown operation ID '{this.Context.OperationId}'");
        return responsefail;
    }

   
}
