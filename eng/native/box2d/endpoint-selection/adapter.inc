// Called only by the generated adapter around the original receipt-history
// solve, including its original live provenance and downstream adjudications.
static bool dango_endpoint_adapter_solve(dango_self_soft_body* body,
    dango_material_contact_diagnostic* diagnostic)
{
    if (!diagnostic->block.built || diagnostic->block.attempted ||
        !dango_endpoint_begin(&diagnostic->endpoint, &diagnostic->block.report.null_reaction)) return false;
    bool solved = dango_rha_solve(body, diagnostic);
    dango_endpoint_end();
    return solved;
}
