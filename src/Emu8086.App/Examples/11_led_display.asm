; LED display on port 199: count from 0 to 1000 in steps of 7.
#start=led_display.exe#
#make_COM#
org 100h

    mov ax, 0
again:
    out 199, ax
    add ax, 7
    cmp ax, 1000
    jbe again
    mov ax, -1234           ; negative numbers work too
    out 199, ax
    ret
