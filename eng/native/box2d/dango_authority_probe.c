/* Isolated executable only. Cold poses are never module exports. */
#include <stdio.h>
int dango_authority_native_tests(void);
int dango_authority_cold_restore_tests(void);
int main(void)
{
    int failures=dango_authority_native_tests();
    failures+=dango_authority_cold_restore_tests();
    printf("{\"Suite\":\"overall\",\"HarnessFailures\":%d,\"CompleteCheckpointSupported\":false}\n",failures);
    return failures==0 ? 0 : 1;
}
