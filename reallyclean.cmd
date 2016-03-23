@echo off

del /q reallyclean.txt reallyclean2.txt 2>nul
for /d /r . %%d in (bin) do if exist "%%d" echo %%d >> reallyclean.txt
for /d /r . %%d in (obj) do if exist "%%d" echo %%d >> reallyclean.txt
type reallyclean.txt | findstr /v node_modules > reallyclean2.txt
for /f %%d in (reallyclean2.txt) do (
  echo %%d
  if exist "%%d" rmdir /s /q "%%d"
)
del /q reallyclean.txt reallyclean2.txt 2>nul