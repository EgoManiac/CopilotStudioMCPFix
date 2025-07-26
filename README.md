# Why MCP Matters, Why SSE Tripped Us Up – And Why Better Debugging Tools Are Overdue  
### A Power Automate Connector Tale by Chris Burns (IT Consultant, UK)

**Keywords:** MCP protocol, Server‑Sent Events (SSE), Power Automate custom connector, Python MCP, debugging, SSE transformation

---

**EDIT:** This has now been fixed in version 1.12.2 of the Python MCP protocol. I'll leave this here for archive and a reminder to myself how to use the code feature in custom connectors

## Introduction: The Big Picture: MCP – Like USB‑C, But for AI

MCP (Model Context Protocol) is fast becoming the standard way to plug large language models into external tools and services. Think of it like USB‑C for AI—same plug, works anywhere. Whether you’re using Claude, Copilot Studio, or rolling your own Python server, MCP gives you a structured, JSON-RPC 2.0‑based way to expose functions, prompts, and resources to LLMs.

Python is the go-to for MCP servers: easy to get going, reasonably well documented, and actively maintained. So, when we needed to connect a Python MCP server via SSE into a Power Automate custom connector, we assumed it’d be straightforward.

Spoiler: it wasn’t.
---

## The Gotcha: When Your ID Changes Type Mid‑Flight 

We hit a subtle but infuriating issue.

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

And although Microsoft’s own docs show SSE examples with MCP, Power Automate connectors can’t handle them out of the box. No error. No warning. Just silent failure.

---

## Community Wisdom: What Helped Us  

The Power Platform Community was invaluable. Contributors emphasised:

- Power Automate enforces strict content‑type/schema validation  
- SSE must be handled line‑by‑line  
- Python MCP server IDs are "currently" numeric, but MS clients expect string IDs as a return - The MCP spec does not clearly state that "type" should be the same.   

A GitHub MCP issue (#[961](https://github.com/modelcontextprotocol/python-sdk/issues/961)) confirms this is a known incompatibility: Python MCP emits integer IDs, conflicting with Copilot Studio expectations.

---

## Power Automate’s Scripting Black Box  

Now, let’s talk tooling.

Digging into this revealed another major friction – **debugging is non‑existent**. The custom connector’s code editor:

- Does not support step‑through debugging  
- Logs do not surface within SSE pipelines
- Compilation errors are vague, e.g. `$"...”` string interpolation silently fails  
- You have no visibility into request/response history  

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
3. Rebuilds compliant output  

---

## What’s Still Missing? Better Tooling, Microsoft!  

If Microsoft want developers to build richer connectors, they need to invest in the basics:

- SSE support that just works
- A proper debug console (even read-only logs would do)
- Compile-time error visibility
- Call history to see what’s going on under the hood

Right now, we’re flying blind.

---

## Final Takeaways  

1. SSE is special; treat it like streaming, not static JSON  
2. Python MCP is technically spec‑correct, but connector consumers may expect strings instead of just integers (On fix list) 
3. Community matters. Without the forum and GitHub threads, we’d still be guessing.
4. Microsoft: sort your tools out. This could be a brilliant platform—if we could just see what it’s doing.

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

