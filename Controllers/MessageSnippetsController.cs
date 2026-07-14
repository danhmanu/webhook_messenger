using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/v1/message-snippets")]
public sealed class MessageSnippetsController(AppDatabase database) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (await AdminAuth.GetSessionAsync(Request, database, cancellationToken) is null)
        {
            return Unauthorized();
        }

        var snippets = await database.GetAllSnippetsAsync(cancellationToken);
        return Ok(snippets.OrderByDescending(snippet => snippet.UpdatedAt));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken cancellationToken)
    {
        if (await AdminAuth.GetSessionAsync(Request, database, cancellationToken) is null)
        {
            return Unauthorized();
        }

        var snippet = await database.GetSnippetByIdAsync(id, cancellationToken);
        return snippet is null ? NotFound() : Ok(snippet);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] MessageSnippetUpsert input, CancellationToken cancellationToken)
    {
        if (await AdminAuth.GetSessionAsync(Request, database, cancellationToken) is null)
        {
            return Unauthorized();
        }

        var result = await database.CreateSnippetAsync(input, cancellationToken);

        if (result is null)
        {
            return BadRequest(new { error = "title_and_content_required" });
        }

        return Created($"/api/v1/message-snippets/{result.Id}", result);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] MessageSnippetUpsert input, CancellationToken cancellationToken)
    {
        if (await AdminAuth.GetSessionAsync(Request, database, cancellationToken) is null)
        {
            return Unauthorized();
        }

        var result = await database.UpdateSnippetAsync(id, input, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPatch("{id}/activation")]
    public async Task<IActionResult> SetActivation(string id, [FromBody] MessageSnippetActivation input, CancellationToken cancellationToken)
    {
        if (await AdminAuth.GetSessionAsync(Request, database, cancellationToken) is null)
        {
            return Unauthorized();
        }

        var result = await database.SetSnippetActiveAsync(id, input.IsActive, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        if (await AdminAuth.GetSessionAsync(Request, database, cancellationToken) is null)
        {
            return Unauthorized();
        }

        return await database.DeleteSnippetAsync(id, cancellationToken) ? NoContent() : NotFound();
    }
}
