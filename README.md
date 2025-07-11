# Why MCP Matters, Why SSE Tripped Us Up – And Why Better Debugging Tools Are Overdue  
### A Power Automate Connector Tale by Chris Burns (IT Consultant, UK)

**Keywords:** MCP protocol, Server‑Sent Events (SSE), Power Automate custom connector, Python MCP, debugging, SSE transformation

---

## Introduction: MCP – The USB‑C of AI Integrations  

The **Model Context Protocol (MCP)** has become the de‑facto standard for connecting Large Language Models to external tools and data—Anthropic even likens it to the **“USB‑C port for AI”**. From Python‑written MCP servers to IDE‑integrated clients in Claude or Copilot Studio, MCP enables a streamlined, cross‑platform architecture: LLMs speak JSON‑RPC 2.0, and MCP servers expose tools, prompts, and resources via **stdio** or **Server‑Sent Events (SSE)**.

Python is the flagship MCP server ecosystem—easy to set up, well‑supported, and widely adopted. But when we integrated a Python MCP server via SSE into a Power Automate custom connector for Copilot Studio, things didn’t go smoothly.

---

## The Real‑World Problem: ID Field Changing Type Mid‑Stream  

Our Power Automate flow expected the `id` field in the MCP response as a **string**, to comply with our connector’s OpenAPI schema. However, a Python MCP server, by default, emits:

```http
event: message
data: {"jsonrpc":"2.0","id":1,"result":{…}}
```

That numeric `"id": 1` failed downstream schema validation, causing connector errors. We needed:

```http
event: message
data: {"jsonrpc":"2.0","id":"1","result":{…}}
```

Simple enough, but the complexity of SSE format and the constraints of custom connector scripting turned this into a much deeper challenge.

---

## SSE ≠ JSON – And Power Automate Won’t Tell You  

Our initial naïve implementation simply parsed the response as JSON via `JObject.Parse(responseString)` and re‑wrote it. Unsurprisingly, it failed catastrophically. That’s when we realised:

- SSE responses use line‑based streaming—`event:` and `data:` lines—not pure JSON  
- You must manually parse out `data:`, transform the JSON, and rebuild with correct headers  

Despite demos using SSE in MCP docs, we learned the hard way that Power Automate custom connectors cannot parse SSE out‑of‑the‑box.

---

## Community Wisdom: What Helped Us  

The Power Platform Community thread (ID `7ec056e9-f950-f011-877a-7c1e5247028a`) was invaluable. Contributors like **wobbdobbs** and **abm** emphasised:

- Power Automate enforces strict content‑type/schema validation  
- SSE must be handled line‑by‑line  
- Python MCP server IDs are "currently" numeric, but MS clients expect string IDs as a return - The MCP spec does not clearly state that "type" should be the same.   

A GitHub MCP issue (#961) confirms this is a known incompatibility: Python MCP emits integer IDs, conflicting with Copilot Studio expectations.

---

## Power Automate’s Scripting Black Box  

Digging into this revealed another major friction – **debugging is non‑existent**. The custom connector’s code editor:

- **Does not support step‑through debugging**  
- **Logs do not surface within SSE pipelines**  
- **Compilation errors are vague**, e.g. `$"...”` string interpolation silently fails  
- You have **no visibility into request/response history**  

These limitations meant we were effectively flying blind, guessing at transformations and hoping they aligned with schema rules. A simple console or call‑history viewer could have saved us hours.

---

## The Working Solution: Code Sample + Explanation  

Here’s the key snippit of code for the custom connector code that finally works - for full code, look at the script file:

```csharp
 //// Shortened - Ensure you use full script file attached

//......

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
   

```

**Why it works:**
1. Extracts only the SSE JSON payload  
2. Converts `id` to string  
3. Rebuilds compliant SSE output  
4. Correctly sets `text/event-stream` Content-Type  

---

## What’s Still Missing? Better Tooling, Microsoft!  

- **Line‑by‑line SSE logging** inside the connector  
- **Built‑in request/response inspector**  
- **Better compile‑time feedback** (e.g. catching `$` syntax errors early)  
- **Support for breakpoints** or output windows  

Even minimal call history or console logs could dramatically reduce dev frustration and errors.

---

## Final Takeaways  

1. **Understand your protocol**—SSE is special; treat it like streaming, not static JSON  
2. **Validate ID types**—Python MCP is technically spec‑correct, but connector consumers may expect strings instead of just integers (On fix list) 
3. **Use community and docs**—MCP design rationale and Power Platform behaviour are key context  
4. **Push for better tooling**—Microsoft, your design‑time experience for connectors needs a revolution  

---

## Thank You to the Community  

Huge thanks to **CU24061549-1** and **FW‑24061242‑0** for their early insights. The GitHub MCP numbering issue (#961) clarified that this isn’t a one‑off bug—it’s a spec‑level mismatch.

---

## Call to Action  

- If you’re building **Python MCP servers**, **test for SSE compatibility**  
- For **Power Automate developers**, share your connector debugging experiences  
- And Microsoft: **give us better logs and breakpoints**, please!

---

**About Chris Burns**  
Technical Architect and AI Automation Design Agency Owner (Kaizenova/Nasstar) from the UK specialising in Power Platform & Copilot Studio, Azure and AI integration.

---

*Published: 2025‑07‑09*

