#include <stdio.h>
int dango_static_platform_contract_tests(void);
int dango_static_platform_failure_tests(void);

int main(void)
{
    int failures=dango_static_platform_contract_tests();
    printf("Static platform support boundary failures: %d\n",failures);
    int failure_path=dango_static_platform_failure_tests();
    printf("Static platform last-safe, fault lock, reset and allocation failures: %d\n",failure_path);
    failures+=failure_path;
    return failures==0 ? 0 : 1;
}
