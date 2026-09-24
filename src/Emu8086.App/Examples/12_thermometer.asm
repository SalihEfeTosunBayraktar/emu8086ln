; Thermostat: keep the temperature between 60 and 80 degrees.
; Port 125 = temperature (read), port 127 = heater (1 on / 0 off).
#start=thermometer.exe#
#make_COM#
org 100h

control:
    in al, 125
    cmp al, 60
    jl heat_on
    cmp al, 80
    jle control
heat_off:
    mov al, 0
    out 127, al
    jmp control
heat_on:
    mov al, 1
    out 127, al
    jmp control
