---
title: logging
priority: 40
---

all errors or warnings need to be written to a log with date and time
create .blaxquad/logs/
one file per session

Microsoft.Extensions.Logging.Abstractions so that main code does not depend on particular logging framework
then choose an appropriate logging impl

i saw the HQ crashing once - make esp. sure that we have last chance exception handler which logs any "fallen through" exception.

even with logging introduced keep existing UI error message

every error message shown in UI also needs to be in log file

as of now we dont need verbose info or debug logging - only add key information
which might be necessary to analyse a crash

